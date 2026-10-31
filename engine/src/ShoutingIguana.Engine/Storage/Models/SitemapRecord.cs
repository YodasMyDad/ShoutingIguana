namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false);
