namespace ShoutingIguana.Core.Configuration;

public enum UserAgentType
{
    Chrome,
    Firefox,
    Edge,
    Safari,
    Random
}

public static class UserAgentConstants
{
    // Latest user agent strings as of 2025
    public const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public const string Firefox = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0";
    public const string Edge = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";
    public const string Safari = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15";

    [ThreadStatic]
    private static Random? _threadRandom;
    private static readonly string[] AllUserAgents = { Chrome, Firefox, Edge, Safari };

    public static string GetRandomUserAgent()
    {
        // Use thread-local Random to avoid thread-safety issues
        _threadRandom ??= new Random(Guid.NewGuid().GetHashCode());
        return AllUserAgents[_threadRandom.Next(AllUserAgents.Length)];
    }
}

public class ProjectSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public int MaxCrawlDepth { get; set; } = 5;
    public int MaxUrlsToCrawl { get; set; } = 1000;
    public bool RespectRobotsTxt { get; set; } = true;
    public bool UseSitemapXml { get; set; } = true;
    public UserAgentType UserAgentType { get; set; } = UserAgentType.Chrome;
    public double CrawlDelaySeconds { get; set; } = 1.0;
    public int ConcurrentRequests { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 10;
    
    /// <summary>
    /// Project-specific proxy settings. If null, uses global proxy from app settings.
    /// </summary>
    public ProxySettings? ProxyOverride { get; set; }

    /// <summary>
    /// Gets the actual user agent string based on the selected type.
    /// If Random is selected, returns a random user agent each time.
    /// </summary>
    public string GetUserAgentString()
    {
        return UserAgentType switch
        {
            UserAgentType.Chrome => UserAgentConstants.Chrome,
            UserAgentType.Firefox => UserAgentConstants.Firefox,
            UserAgentType.Edge => UserAgentConstants.Edge,
            UserAgentType.Safari => UserAgentConstants.Safari,
            UserAgentType.Random => UserAgentConstants.GetRandomUserAgent(),
            _ => UserAgentConstants.Chrome
        };
    }
}

