namespace WebCrawlerApp.Models
{
    public class IndexEntry
    {
        public string RelevantUrl { get; set; } = "";
        public string OriginUrl { get; set; } = "";
        public int Depth { get; set; }
        public int WordCount { get; set; } // Relevancy/Score
    }
}