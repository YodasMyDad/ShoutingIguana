namespace ShoutingIguana.Core.Services;

public interface ICrawlEngine : IDisposable
{
    Task StartCrawlAsync(int projectId, CancellationToken cancellationToken = default);
    Task StopCrawlAsync();
    Task PauseCrawlAsync();
    Task ResumeCrawlAsync();
    bool IsCrawling { get; }
    bool IsPaused { get; }
    event EventHandler<CrawlProgressEventArgs>? ProgressUpdated;
    
    /// <summary>
    /// Checks if there is an active checkpoint for a project (for crash recovery).
    /// </summary>
    Task<ShoutingIguana.Core.Models.CrawlCheckpoint?> GetActiveCheckpointAsync(int projectId);
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

