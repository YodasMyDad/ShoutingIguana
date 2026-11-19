namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="IsProbe">Nothing declared a sitemap, so the crawler tried the conventional /sitemap.xml.</param>
/// <param name="ListedSitemaps">How many sitemaps the file lists, as read; null when its body was never read.</param>
/// <param name="TurnedAway">
/// The site turned the read away: it answered 429 at every try, or a firewall's challenge came in the file's place. A
/// crawl stores that as the sitemap's status; a recheck, which has the file as the crawl read it, takes it as no answer.
/// </param>
internal sealed record SitemapRecord(int Id, int? StatusCode, bool IsIndex, int UrlCount, string? Error, bool IsProbe = false, int? ListedSitemaps = null,
    long? SizeBytes = null, bool TurnedAway = false);
