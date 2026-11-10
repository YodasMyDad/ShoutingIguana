namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How far a crawl got with its sitemaps.</summary>
public enum SitemapDiscovery
{
    /// <summary>The crawl was told not to read sitemaps.</summary>
    Off,

    /// <summary>The crawl is running, and still reading them.</summary>
    Running,

    /// <summary>The crawl stopped before it had read them all: the ones it has aren't all there are.</summary>
    Unfinished,

    Done
}
