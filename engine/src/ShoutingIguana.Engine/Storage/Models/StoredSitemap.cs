namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="Error">Why the sitemap could not be read, when it could not.</param>
/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
internal sealed record StoredSitemap(string Url, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false);
