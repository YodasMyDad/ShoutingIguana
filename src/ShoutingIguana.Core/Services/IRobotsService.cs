namespace ShoutingIguana.Core.Services;

public interface IRobotsService
{
    Task<bool> IsAllowedAsync(string url, string userAgent, HttpClient? httpClient = null);
    Task<double?> GetCrawlDelayAsync(string host, string userAgent, HttpClient? httpClient = null);
    Task<List<string>> GetSitemapUrlsFromRobotsTxtAsync(string host, HttpClient? httpClient = null);
}

