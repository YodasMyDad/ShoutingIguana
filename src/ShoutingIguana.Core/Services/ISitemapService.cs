namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for discovering and parsing XML sitemaps.
/// </summary>
public interface ISitemapService
{
    /// <summary>
    /// Discovers sitemap URLs by checking robots.txt and common sitemap locations.
    /// </summary>
    /// <param name="baseUrl">The base URL of the site to check for sitemaps.</param>
    /// <returns>A list of URLs found in the sitemap(s).</returns>
    Task<List<string>> DiscoverSitemapUrlsAsync(string baseUrl);
}

