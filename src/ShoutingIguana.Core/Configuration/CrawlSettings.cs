using System.Runtime.Versioning;

namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Global crawl defaults, persisted in <c>%LocalAppData%\ShoutingIguana\settings.json</c>.
/// Any field also present on <see cref="ProjectSettings"/> is overridden per-project when a
/// crawl runs — these values act as the "new project" defaults surfaced in the Settings view.
/// Fields that are not mirrored on <see cref="ProjectSettings"/> (e.g., <see cref="MemoryLimitMb"/>,
/// <see cref="CheckpointInterval"/>, <see cref="RetryCount"/>, <see cref="ConnectionTimeoutSeconds"/>)
/// apply globally for every crawl.
/// </summary>
[SupportedOSPlatform("windows")]
public class CrawlSettings
{
    public int ConcurrentRequests { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxCrawlDepth { get; set; } = 5;
    public int MaxUrlsToCrawl { get; set; } = 10000;
    public bool RespectRobotsTxt { get; set; } = true;
    public bool UseSitemapXml { get; set; } = true;
    public double CrawlDelaySeconds { get; set; } = 1.5;
    public int MemoryLimitMb { get; set; } = 1536;
    public int RetryCount { get; set; } = 3;
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int CheckpointInterval { get; set; } = 50;

    /// <summary>
    /// Global proxy settings. A project with <see cref="ProjectSettings.ProxyOverride"/> set
    /// to non-null ignores this value entirely.
    /// </summary>
    public ProxySettings GlobalProxy { get; set; } = new();
}

