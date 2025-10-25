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
    public bool UseSitemapXml { get; set; } = true;
    public double CrawlDelaySeconds { get; set; } = 1.0;
    public int MemoryLimitMb { get; set; } = 1536; // 1.5 GB default
    public int RetryCount { get; set; } = 3;
}

