namespace ShoutingIguana.Engine.Sitemaps.Models;

/// <param name="IsSitemap">The entry is a child sitemap listed by a sitemap index, not a page.</param>
internal sealed record SitemapEntry(string Location, bool IsSitemap);
