using ShoutingIguana.Engine.Sitemaps;

namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="Error">Why the sitemap could not be read, when it could not.</param>
/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
internal sealed record StoredSitemap(string Url, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false)
{
    /// <summary>How many sitemaps the file lists; null when its body wasn't read, or an earlier engine read it.</summary>
    public int? ListedSitemaps { get; init; }

    /// <summary>The file's size as read, uncompressed; null as <see cref="ListedSitemaps"/> is.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>It answered 2xx and was read without an error.</summary>
    public bool IsRead => Error is null && StatusCode is >= 200 and < 300;

    private const string NOT_READ_PREFIX = SitemapDiscoverer.NOT_READ_ERROR + ": ";

    /// <summary>Why the crawler chose not to read this sitemap, being past its limits; null when it tried to.</summary>
    public string? NotReadReason =>
        Error is { } error && error.StartsWith(NOT_READ_PREFIX, StringComparison.Ordinal) ? error[NOT_READ_PREFIX.Length..] : null;
}
