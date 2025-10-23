namespace ShoutingIguana.Core.Configuration;

public class CrawlSettings
{
    public int MaxDepth { get; set; } = 5;
    public int MaxUrls { get; set; } = 1000;
    public bool RespectRobotsTxt { get; set; } = true;
    public string UserAgent { get; set; } = "ShoutingIguana/1.0";
    public double CrawlDelaySeconds { get; set; } = 1.0;
    public int ConcurrentRequests { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
}

