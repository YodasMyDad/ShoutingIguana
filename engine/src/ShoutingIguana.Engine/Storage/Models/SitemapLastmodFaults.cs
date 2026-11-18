namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A sitemap's entries whose lastmod is in the future, or isn't a date (<see cref="Sitemaps.SitemapLastmod"/>).</summary>
/// <param name="FirstUrl">The first such entry's URL, in crawl order; null where it isn't known.</param>
/// <param name="FirstLastmod">
/// That entry's lastmod as Unix seconds, or <see cref="Sitemaps.SitemapLastmod.UNREADABLE"/> for one that isn't a date.
/// </param>
internal sealed record SitemapLastmodFaults(string SitemapUrl, int Future, int Unreadable, string? FirstUrl = null, long? FirstLastmod = null);
