namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a crawl came by a sitemap.</summary>
public enum SitemapSource
{
    /// <summary>The start URL's robots.txt declares it.</summary>
    RobotsTxt,

    /// <summary>The crawl's options gave it.</summary>
    Options,

    /// <summary>Nothing declared a sitemap, so the crawler tried the conventional <c>/sitemap.xml</c>.</summary>
    DefaultLocation,

    /// <summary>A sitemap index lists it.</summary>
    Index
}
