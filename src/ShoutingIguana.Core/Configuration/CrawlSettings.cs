namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Default crawl settings.
/// </summary>
public class CrawlSettings
{
    public int ConcurrentRequests { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxCrawlDepth { get; set; } = 5;
    public int MaxUrlsToCrawl { get; set; } = 10000;
    public bool RespectRobotsTxt { get; set; } = true;
    public double CrawlDelaySeconds { get; set; } = 1.0;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    public int MemoryLimitMb { get; set; } = 1536; // 1.5 GB default
    public int RetryCount { get; set; } = 3;
}

