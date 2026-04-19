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

/// <summary>
/// Per-project crawl configuration, persisted to that project's SQLite database as JSON
/// (<c>Project.SettingsJson</c>). These values override the global <see cref="CrawlSettings"/>
/// defaults whenever a crawl runs — the project-level value always wins, even if it matches
/// the default. Global <see cref="CrawlSettings"/> are only consulted for fields that are
/// not mirrored here (e.g., <see cref="CrawlSettings.MemoryLimitMb"/>,
/// <see cref="CrawlSettings.CheckpointInterval"/>, <see cref="CrawlSettings.RetryCount"/>)
/// and as a source for <see cref="CrawlSettings.GlobalProxy"/> when
/// <see cref="ProxyOverride"/> is null.
/// </summary>
/// <remarks>
/// Intentionally a plain class (not inheriting <see cref="CrawlSettings"/>) to keep the JSON
/// shape stable across upgrades — existing project <c>.db</c> files round-trip through
/// <see cref="System.Text.Json"/> with the fields declared here.
/// </remarks>
public class ProjectSettings
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Overrides <see cref="CrawlSettings.MaxCrawlDepth"/> for this project.</summary>
    public int MaxCrawlDepth { get; set; } = 5;

    /// <summary>Overrides <see cref="CrawlSettings.MaxUrlsToCrawl"/> for this project.</summary>
    public int MaxUrlsToCrawl { get; set; } = 1000;

    /// <summary>Overrides <see cref="CrawlSettings.RespectRobotsTxt"/> for this project.</summary>
    public bool RespectRobotsTxt { get; set; } = true;

    /// <summary>Overrides <see cref="CrawlSettings.UseSitemapXml"/> for this project.</summary>
    public bool UseSitemapXml { get; set; } = true;

    public UserAgentType UserAgentType { get; set; } = UserAgentType.Chrome;

    /// <summary>
    /// Overrides <see cref="CrawlSettings.CrawlDelaySeconds"/> for this project. Applied
    /// per-host (not per-worker) via <c>HostThrottle</c>.
    /// </summary>
    public double CrawlDelaySeconds { get; set; } = 1.5;

    /// <summary>Overrides <see cref="CrawlSettings.ConcurrentRequests"/> for this project.</summary>
    public int ConcurrentRequests { get; set; } = 4;

    /// <summary>Overrides <see cref="CrawlSettings.TimeoutSeconds"/> for this project.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Project-specific proxy settings. If null, uses <see cref="CrawlSettings.GlobalProxy"/>
    /// from app settings. TODO (Wave 4): add ConfigurationPrecedenceTests covering
    /// project-overrides-global and proxy-fallback round-trip scenarios.
    /// </summary>
    public ProxySettings? ProxyOverride { get; set; }

    /// <summary>
    /// Allow the crawler to fetch URLs whose host is loopback, private (RFC1918),
    /// link-local, or otherwise non-routable. Off by default to prevent SSRF-style
    /// abuse. Enable for staging sites on internal IPs.
    /// </summary>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>
    /// When true (the default, matching Screaming Frog), the crawler aborts requests
    /// for images, media, and fonts so pages fetch only the resources needed for link
    /// and metadata extraction. Cuts page weight by 60–80% on typical sites.
    /// </summary>
    public bool BlockNonEssentialResources { get; set; } = true;

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

