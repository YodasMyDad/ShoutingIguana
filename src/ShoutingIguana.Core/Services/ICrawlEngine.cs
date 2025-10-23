namespace ShoutingIguana.Core.Services;

public interface ICrawlEngine
{
    Task StartCrawlAsync(int projectId, CancellationToken cancellationToken = default);
    Task StopCrawlAsync();
    bool IsCrawling { get; }
    event EventHandler<CrawlProgressEventArgs>? ProgressUpdated;
}

public class CrawlProgressEventArgs : EventArgs
{
    public int UrlsCrawled { get; set; }
    public int TotalDiscovered { get; set; }
    public int QueueSize { get; set; }
    public int ActiveWorkers { get; set; }
    public int ErrorCount { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? LastCrawledUrl { get; set; }
}

