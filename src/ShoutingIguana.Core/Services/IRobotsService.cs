namespace ShoutingIguana.Core.Services;

public interface IRobotsService
{
    Task<bool> IsAllowedAsync(string url, string userAgent);
    Task<double?> GetCrawlDelayAsync(string host, string userAgent);
}

