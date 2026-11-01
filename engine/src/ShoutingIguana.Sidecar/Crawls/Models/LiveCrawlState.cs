namespace ShoutingIguana.Sidecar.Crawls.Models;

/// <summary>What this process is doing with a crawl.</summary>
internal enum LiveCrawlState
{
    /// <summary>Opening its file, which reads every URL into memory when it continues: the library shows it running.</summary>
    Starting,
    Running,
    Paused,
    Stopping,
    Analyzing
}
