using ShoutingIguana.Engine.Sitemaps;

namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="Error">Why the sitemap could not be read, when it could not.</param>
/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
internal sealed record StoredSitemap(string Url, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false)
{
    private const string NOT_READ_PREFIX = SitemapDiscoverer.NOT_READ_ERROR + ": ";

    /// <summary>Why the crawler chose not to read this sitemap, being past its limits; null when it tried to.</summary>
    public string? NotReadReason =>
        Error is { } error && error.StartsWith(NOT_READ_PREFIX, StringComparison.Ordinal) ? error[NOT_READ_PREFIX.Length..] : null;
}
