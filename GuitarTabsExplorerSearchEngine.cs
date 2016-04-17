#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Tabster.Core.Searching;
using Tabster.Core.Types;

#endregion

namespace GuitarTabsExplorer
{
    public class GuitarTabsExplorerSearchEngine : ITablatureSearchEngine
    {
        public GuitarTabsExplorerSearchEngine()
        {
            Homepage = new Uri("http://guitartabsexplorer.com");
        }

        #region Implementation of ISearchService

        public string Name
        {
            get { return "Guitar Tabs Explorer"; }
        }

        public Uri Homepage { get; private set; }

        public bool RequiresArtistParameter
        {
            get { return false; }
        }

        public bool RequiresTitleParameter
        {
            get { return false; }
        }

        public bool RequiresTypeParamter
        {
            get { return false; }
        }

        public bool SupportsRatings
        {
            get { return true; }
        }

        public bool SupportsPrefilteredTypes { get; private set; }
        public int MaximumConsecutiveRequests { get; private set; }

        public TablatureSearchResult[] Search(TablatureSearchQuery query, WebProxy proxy = null)
        {
            // song search URL format
            // http://www.guitartabsexplorer.com/search.php?search=smells+like+teen+spirit

            // artist search URL format
            // http://www.guitartabsexplorer.com/nirvana-Tabs/1/

            var results = new List<TablatureSearchResult>();

            // only artist without title
            if (!string.IsNullOrEmpty(query.Artist) && string.IsNullOrEmpty(query.Title))
            {
                var currentPage = 1;
                int totalPages;

                do
                {
                    results.AddRange(ProcessArtistSearch(query, proxy, currentPage, out totalPages));
                    currentPage++;
                } while (currentPage <= totalPages);
            }

            else
            {
                results.AddRange(ProcessTitleSearch(query, proxy));
            }

            return results.ToArray();
        }

        public bool SupportsTabType(TablatureType type)
        {
            return type == TablatureType.Guitar || type == TablatureType.Chords || type == TablatureType.Bass;
        }

        private IEnumerable<TablatureSearchResult> ProcessTitleSearch(TablatureSearchQuery query, WebProxy proxy)
        {
            var results = new List<TablatureSearchResult>();

            var sb = new StringBuilder("http://www.guitartabsexplorer.com/search.php?search=");
            var urlTitle = HttpUtility.UrlEncode(query.Title).Replace("%20", "+");
            sb.Append(urlTitle);

            string html;
            using (var client = new WebClient {Proxy = proxy})
            {
                html = client.DownloadString(sb.ToString());
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var matchingSongsTitleNode = doc.DocumentNode.SelectSingleNode("//h3[text()='Matching songs']");

            var resultsContainer = matchingSongsTitleNode.NextSibling.NextSibling;

            var resultsLists = resultsContainer.SelectNodes(".//ul");

            foreach (var list in resultsLists)
            {
                foreach (var item in list.SelectNodes("li"))
                {
                    var artist = item.SelectSingleNode("a").InnerText;

                    if (!artist.Equals(query.Artist, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var link = item.SelectSingleNode("strong/a");

                    var url = link.Attributes["href"].Value;
                    var title = link.InnerText;

                    // this should probably be done via text()
                    var type = GetTypeFromString(item.InnerHtml.Substring(item.InnerHtml.LastIndexOf("(")).Replace("(", "").Replace(")", ""));

                    if (query.Type != null && query.Type != type)
                        continue;

                    // todo rating, possibly via pre-fetching?

                    var tab = new AttributedTablature(artist, title, type);
                    results.Add(new TablatureSearchResult(query, this, tab,
                        new Uri(string.Format("http://www.guitartabsexplorer.com{0}", url))));
                }
            }

            return results.ToArray();
        }

        private IEnumerable<TablatureSearchResult> ProcessArtistSearch(TablatureSearchQuery query, WebProxy proxy, int currentPage, out int totalPages)
        {
            var sb = new StringBuilder("http://www.guitartabsexplorer.com/");
            var artist = query.Artist.Replace(" ", "-");
            artist = HttpUtility.UrlEncode(artist);
            sb.Append(artist + "-Tabs/");
            sb.Append(currentPage);

            if (query.Type != null)
            {
                if (query.Type == TablatureType.Guitar)
                    sb.Append("/tabs");
                if (query.Type == TablatureType.Chords)
                    sb.Append("/chords");
                if (query.Type == TablatureType.Bass)
                    sb.Append("/bass");
            }

            sb.Append("/");

            var results = new List<TablatureSearchResult>();

            string html;
            using (var client = new WebClient {Proxy = proxy})
            {
                html = client.DownloadString(sb.ToString());
            }

            var pattern = new Regex(@"Browse our (?<artist>.*?) collection", RegexOptions.Compiled);
            var extractedArtist = pattern.Match(html).Groups["artist"].Value;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // only table with thead
            var thead = doc.DocumentNode.SelectSingleNode("//table/thead");
            var table = thead.ParentNode;

            var rows = table.SelectNodes("tbody/tr");

            foreach (HtmlNode row in rows)
            {
                var link = row.SelectSingleNode("td/a");

                // skip embedded ads
                if (link == null)
                    continue;

                var url = link.Attributes["href"].Value;
                var title = link.InnerText;
                var type = GetTypeFromString(row.SelectSingleNode("td[2]").InnerText);

                if (query.Type != null && query.Type != type)
                    continue;

                var starNodes = row.SelectNodes("td[3]/i[@class='fa fa-star']");
                var rating = starNodes == null ? TablatureRating.None : TablatureRatingUtilities.FromInt(starNodes.Count);

                var tab = new AttributedTablature(extractedArtist, title, type);
                results.Add(new TablatureSearchResult(query, this, tab,
                    new Uri(string.Format("http://www.guitartabsexplorer.com{0}", url)), rating));
            }

            // pagination is in element after table (after skipping text node)
            var pagination = table.NextSibling.NextSibling;

            if (pagination != null)
            {
                var lastItem = pagination.ChildNodes[pagination.ChildNodes.Count - 1];

                if (lastItem.InnerText.Trim().Equals("Next", StringComparison.OrdinalIgnoreCase))
                    lastItem = pagination.ChildNodes[pagination.ChildNodes.Count - 3]; // decrement 3 times to count for text node


                int.TryParse(lastItem.InnerText, out totalPages);
            }

            else
                totalPages = currentPage;

            return results.ToArray();
        }

        #endregion

        private static TablatureType GetTypeFromString(string str)
        {
            if (str.IndexOf("bass", StringComparison.OrdinalIgnoreCase) >= 0)
                return TablatureType.Bass;
            if (str.IndexOf("chords", StringComparison.OrdinalIgnoreCase) >= 0)
                return TablatureType.Chords;
            if (str.IndexOf("tabs", StringComparison.OrdinalIgnoreCase) >= 0)
                return TablatureType.Guitar;

            return null;
        }
    }
}