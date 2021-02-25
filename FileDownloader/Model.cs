using System.Collections.Generic;

namespace FileDownloader
{
    internal class Model
    {
        public string DownloadPath { get; set; }
        public IDictionary<string, string> Urls { get; set; }
    }
}