using System.Globalization;

namespace ShoutingIguana.Engine.Sitemaps;

/// <summary>
/// A sitemap entry's <c>&lt;lastmod&gt;</c> as stored (<see cref="Storage.CrawlSchema.SITEMAP_URL_COLUMNS"/>): Unix seconds,
/// <see cref="UNREADABLE"/> when it isn't a date, null when there is none. sitemaps.org asks for W3C Datetime (a date, or
/// a date and time with its zone); a time without a zone, or with a +hhmm one, is read too (as UTC, and as written), since
/// search engines read those, so only what no reader could take as a date is unreadable.
/// </summary>
internal static class SitemapLastmod
{
    public const long UNREADABLE = -1;

    private static readonly string[] FORMATS =
    [
        "yyyy", "yyyy-MM", "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ssK", "yyyy-MM-dd HH:mm:sszzz"
    ];

    public static long? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var written = text.Trim();

        // A +hhmm zone (ISO 8601's basic form) reads as +hh:mm.
        if (written.Length > 5 && written[^5] is '+' or '-' && written[^4..].All(char.IsAsciiDigit) && written.Contains('T', StringComparison.Ordinal))
        {
            written = $"{written[..^2]}:{written[^2..]}";
        }

        return DateTimeOffset.TryParseExact(written, FORMATS, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUnixTimeSeconds()
            : UNREADABLE;
    }
}
