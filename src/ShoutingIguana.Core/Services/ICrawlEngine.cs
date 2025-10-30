namespace ShoutingIguana.Core.Services;

public interface ICrawlEngine : IDisposable
{
    Task StartCrawlAsync(int projectId, bool resumeFromCheckpoint = false, CancellationToken cancellationToken = default);
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

/// <summary>
/// Represents the current phase of the crawl operation.
/// </summary>
public enum CrawlPhase
{
    /// <summary>
    /// Phase 1: Discovering and crawling URLs, extracting links, saving to database.
    /// </summary>
    Discovery = 0,
    
    /// <summary>
    /// Phase 2: Analyzing crawled URLs, executing plugins, reporting findings.
    /// </summary>
    Analysis = 1
}

public class CrawlProgressEventArgs : EventArgs
{
    public CrawlPhase CurrentPhase { get; set; }
    public int UrlsCrawled { get; set; }
    public int UrlsAnalyzed { get; set; }
    public int TotalDiscovered { get; set; }
    public int QueueSize { get; set; }
    public int ActiveWorkers { get; set; }
    public int ErrorCount { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string? LastCrawledUrl { get; set; }
}

