#region

using System;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Tabster.Core.Types;
using Tabster.Data.Processing;

#endregion

namespace GuitarTabsExplorer
{
    public class GuitarTabsExplorerParser : ITablatureWebImporter
    {
        #region Implementation of ITablatureWebImporter

        public AttributedTablature Parse(Uri url, WebProxy proxy)
        {
            string html;
            using (var client = new WebClient {Proxy = proxy})
            {
                html = client.DownloadString(url);
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var typePattern = new Regex(@"<h2>(?<type>.*?) :</h2>", RegexOptions.Compiled);
            var extractedType = GetTypeFromString(typePattern.Match(html).Groups["type"].Value);

            var artistPattern = new Regex(@"<h1>\""(?<title>.*?)\"" by (?<artist>.*?)</h1>", RegexOptions.Compiled);
            var extractedArtist = artistPattern.Match(html).Groups["artist"].Value;
            var extractedTitle = artistPattern.Match(html).Groups["title"].Value;

            var ratingPanel = doc.DocumentNode.SelectSingleNode("//div[@class='panel show-for-medium-up'][2]");
            var rating = ratingPanel.SelectNodes(".//img[@src='/images/star.png']");

            var contents = doc.DocumentNode.SelectSingleNode("//pre").InnerText;

            //todo use rating

            if (extractedType == null || contents == null)
                return null;

            return new AttributedTablature(extractedArtist, extractedTitle, extractedType) {Contents = contents};
        }

        public string SiteName
        {
            get { return "Guitar Tabs Explorer"; }
        }

        public Uri Homepage
        {
            get { return new Uri("http://guitartabsexplorer.com"); }
        }

        public Version Version
        {
            get { return new Version("1.0"); }
        }

        public bool IsUrlParsable(Uri url)
        {
            return url.DnsSafeHost == "guitartabsexplorer.com" || url.DnsSafeHost == "www.guitartabsexplorer.com";
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