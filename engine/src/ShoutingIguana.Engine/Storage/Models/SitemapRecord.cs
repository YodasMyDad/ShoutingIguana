namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
/// <param name="ListedSitemaps">How many sitemaps the file lists, as read; null when its body was never read.</param>
internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false, int? ListedSitemaps = null);
