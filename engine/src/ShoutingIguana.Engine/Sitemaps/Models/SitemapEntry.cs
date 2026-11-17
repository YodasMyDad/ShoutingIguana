namespace ShoutingIguana.Engine.Sitemaps.Models;

/// <param name="IsSitemap">The entry is a child sitemap listed by a sitemap index, not a page.</param>
/// <param name="Lastmod">The entry's <c>&lt;lastmod&gt;</c> as written, trimmed; null when it has none (a text sitemap's never do).</param>
internal sealed record SitemapEntry(string Location, bool IsSitemap, string? Lastmod = null);
