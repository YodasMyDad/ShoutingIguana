namespace ShoutingIguana.Core.Configuration;

public class ProjectSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int MaxCrawlDepth { get; set; } = 5;
    public int MaxUrlsToCrawl { get; set; } = 1000;
    public bool RespectRobotsTxt { get; set; } = true;
    public string UserAgent { get; set; } = "ShoutingIguana/1.0";
    public double CrawlDelaySeconds { get; set; } = 1.0;
    public int ConcurrentRequests { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 30;
}

