using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Compares the sitemaps with what the crawl found. Problems with the sitemap files themselves go on the home page.
/// Coverage (pages missing from the sitemaps, no sitemap at all) needs a complete crawl with every sitemap read, and
/// pages missing from the sitemaps can't be judged while a declared sitemap is <see cref="Unreadable"/>.
/// </summary>
/// <param name="offHostEntries">
/// The site's own URLs listed only by sitemaps on another host (<see cref="Storage.AnalysisReader.ReadOffHostSitemapEntries"/>),
/// such as www.example.com pages in example.com's sitemap. URLs off the site are always on another host.
/// </param>
internal sealed class SitemapRules(UrlTable urls, IReadOnlyList<StoredSitemap> sitemaps, CrawlOptions options, bool crawlComplete,
    IReadOnlySet<int>? offHostEntries = null, SitemapEntryFacts? entries = null, GooglebotAccess? googlebot = null) : ISiteRule
{
    private const int LISTED_FAILURES = 3;
    private const int LISTED_FILES = 3;

    /// <summary>The sitemaps a repeated URL's detail names before "and N more".</summary>
    private const int LISTED_REPEATS = 2;

    /// <summary>Google's and sitemaps.org's limits for one file: 50,000 URLs (or sitemaps, for an index), 50 MB uncompressed.</summary>
    internal const int MAX_ENTRIES = 50_000;

    internal const long MAX_BYTES = 50L * 1024 * 1024;

    /// <summary>Near the limit: within a tenth of it (ours; the old app warned at 45,000).</summary>
    internal const int NEAR_ENTRIES = 45_000;

    internal const long NEAR_BYTES = 45L * 1024 * 1024;

    private readonly IReadOnlySet<int> _offHostEntries = offHostEntries ?? new HashSet<int>();
    private readonly SitemapEntryFacts _entries = entries ?? new SitemapEntryFacts();

    /// <summary>
    /// The first sitemap listed in robots.txt or an index that failed for a reason that says nothing about what it
    /// lists: a server error, rate limit, block, timeout or unparseable file, or the crawler's limits, which leave it
    /// unread. A 404 or 410 is different: that sitemap doesn't exist, so pages it was meant to list really are in no
    /// sitemap a search engine can read.
    /// </summary>
    public static StoredSitemap? Unreadable(IReadOnlyList<StoredSitemap> sitemaps) =>
        sitemaps.FirstOrDefault(sitemap => sitemap is { Error: not null, IsProbe: false, StatusCode: not (404 or 410) });

    public IReadOnlyList<IssueDefinition> Checks { get; } =
    [
        IssueCatalog.SITEMAP_URL_OTHER_HOST, IssueCatalog.SITEMAP_NON_INDEXABLE, IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.SITEMAP_ERROR, IssueCatalog.SITEMAP_MISSING,
        IssueCatalog.SITEMAP_OVER_LIMIT, IssueCatalog.SITEMAP_NEAR_LIMIT, IssueCatalog.SITEMAP_EMPTY, IssueCatalog.SITEMAP_URL_REPEATED,
        IssueCatalog.SITEMAP_LASTMOD_INVALID
    ];

    public void Observe(AnalyzedPage page)
    {
    }

    public IEnumerable<IssueHit> Finish()
    {
        var checkCoverage = crawlComplete && Unreadable(sitemaps) is null &&
                            sitemaps.Any(sitemap => sitemap is { Error: null, StatusCode: >= 200 and < 300 });
        for (var id = 1; id < urls.Length; id++)
        {
            var url = urls[id];

            // sitemaps.org: every URL in a sitemap must be on the sitemap's own host, and scheme, or search engines drop it.
            if (url.InSitemap && (!url.IsInternal || _offHostEntries.Contains(id)))
            {
                yield return new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, id, url.Fetched ? $"The URL {RuleText.Status(url)}" : "The URL wasn't checked");
            }
            else if (url.InSitemap && _entries.OtherScheme.TryGetValue(id, out var schemes))
            {
                yield return new IssueHit(IssueCatalog.SITEMAP_URL_OTHER_HOST, id, $"The URL is {schemes.Url}://, the sitemap {schemes.Sitemap}://");
            }

            if (url.IsInternal && _entries.Repeated.TryGetValue(id, out var listings))
            {
                yield return new IssueHit(IssueCatalog.SITEMAP_URL_REPEATED, id, Repeated(listings));
            }

            if (!url.IsInternal)
            {
                continue;
            }

            if (!url.Fetched)
            {
                continue;
            }

            // A PDF or image listed in a sitemap is fine, and URLs the crawler couldn't check are reported as
            // response problems instead. What's left is pages that can't be indexed.
            // A site that turns every crawler away but the search engines it names refused this crawl, not Google: what
            // its pages are to Google wasn't read, as with a firewall's block.
            var unverified = url.Outcome is FetchOutcome.Blocked or FetchOutcome.Timeout or FetchOutcome.NetworkError or FetchOutcome.ProcessingError ||
                             (url.Outcome == FetchOutcome.RobotsDisallowed && googlebot?.Allows(id) == true);
            var nonIndexable = url.Outcome != FetchOutcome.Success || url.IndexabilityReason != IndexabilityReason.None;
            if (url.InSitemap && nonIndexable && !unverified)
            {
                yield return new IssueHit(IssueCatalog.SITEMAP_NON_INDEXABLE, id, $"The URL {RuleText.Status(url)}");
            }
            else if (checkCoverage && !url.InSitemap && url.IsIndexablePage)
            {
                yield return new IssueHit(IssueCatalog.NOT_IN_SITEMAP, id);
            }
        }

        if (urls.HomeId == 0)
        {
            yield break;
        }

        // A failed guess at /sitemap.xml means there's no sitemap, not a broken one. One the crawler chose not to read
        // isn't broken either.
        var failed = sitemaps.Where(sitemap => sitemap is { Error: not null, IsProbe: false, NotReadReason: null }).ToList();
        if (failed.Count > 0)
        {
            var listed = string.Join(", ", failed.Take(LISTED_FAILURES).Select(sitemap => $"{sitemap.Url} ({HttpStatusNames.NameIn(sitemap.Error!)})"));
            var more = failed.Count > LISTED_FAILURES ? $" and {failed.Count - LISTED_FAILURES:N0} more" : string.Empty;
            yield return new IssueHit(IssueCatalog.SITEMAP_ERROR, urls.HomeId, listed + more) { Files = [.. failed.Select(sitemap => sitemap.Url)] };
        }

        // "None at /sitemap.xml" takes an answer that says so. A site that turned the crawl away at its home page (a
        // firewall's challenge answers every URL alike), and a guess that got no answer, a 429 or a server error, say
        // nothing about whether the file is there.
        var anyFound = sitemaps.Any(sitemap => !sitemap.IsProbe || sitemap.Error is null);
        var unanswered = urls[urls.HomeId].Outcome == FetchOutcome.Blocked ||
                         sitemaps.Any(sitemap => sitemap is { IsProbe: true, Error: not null, StatusCode: null or 429 or >= 500 });
        if (crawlComplete && options.DiscoverSitemaps && !anyFound && !unanswered)
        {
            // Where the crawler looked: the conventional /sitemap.xml it tried, which the Sitemaps screen lists.
            var tried = sitemaps.Where(sitemap => sitemap.IsProbe).Select(sitemap => sitemap.Url).ToList();
            yield return new IssueHit(IssueCatalog.SITEMAP_MISSING, urls.HomeId) { Files = tried.Count > 0 ? tried : null };
        }

        var read = sitemaps.Where(sitemap => sitemap.IsRead).ToList();
        var overFiles = read.Where(sitemap => SizeWords(sitemap, MAX_ENTRIES, MAX_BYTES) is not null).ToList();
        if (Files(overFiles, MAX_ENTRIES, MAX_BYTES) is { } over)
        {
            yield return new IssueHit(IssueCatalog.SITEMAP_OVER_LIMIT, urls.HomeId, over) { Files = [.. overFiles.Select(sitemap => sitemap.Url)] };
        }

        var nearFiles = read.Where(sitemap => SizeWords(sitemap, MAX_ENTRIES, MAX_BYTES) is null && SizeWords(sitemap, NEAR_ENTRIES - 1, NEAR_BYTES - 1) is not null).ToList();
        if (Files(nearFiles, NEAR_ENTRIES - 1, NEAR_BYTES - 1) is { } near)
        {
            yield return new IssueHit(IssueCatalog.SITEMAP_NEAR_LIMIT, urls.HomeId, near) { Files = [.. nearFiles.Select(sitemap => sitemap.Url)] };
        }

        // An index's <sitemap> entries are counted from this version on; a sitemap an earlier one read as an index isn't empty.
        var empty = read.Where(sitemap => sitemap is { UrlCount: 0, IsIndex: false, ListedSitemaps: null or 0 }).Select(sitemap => sitemap.Url).ToList();
        if (empty.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.SITEMAP_EMPTY, urls.HomeId, Listed(empty)) { Files = empty };
        }

        if (_entries.Lastmods.Count > 0)
        {
            var future = _entries.Lastmods.Sum(fault => (long)fault.Future);
            var unreadable = _entries.Lastmods.Sum(fault => (long)fault.Unreadable);
            List<string> kinds = [];
            if (future > 0)
            {
                kinds.Add($"{future:N0} in the future");
            }

            if (unreadable > 0)
            {
                kinds.Add($"{unreadable:N0} unreadable");
            }

            var total = future + unreadable;
            yield return new IssueHit(IssueCatalog.SITEMAP_LASTMOD_INVALID, urls.HomeId,
                $"{RuleText.Count((int)Math.Min(total, int.MaxValue), "entry", "entries")}: {string.Join(", ", kinds)} ({Listed([.. _entries.Lastmods.Select(fault => fault.SitemapUrl)])})" +
                FirstWrongLastmod(_entries.Lastmods[0], total))
            {
                Files = [.. _entries.Lastmods.Select(fault => fault.SitemapUrl)]
            };
        }
    }

    /// <summary>
    /// The first wrong entry, to look at: ": https://x/p/1, lastmod 3000-01-01" for the only one, "; first: https://x/p/9,
    /// lastmod not a date" for the first of several; nothing where it isn't known.
    /// </summary>
    private static string FirstWrongLastmod(SitemapLastmodFaults first, long total)
    {
        if (first is not { FirstUrl: { } url, FirstLastmod: { } lastmod })
        {
            return string.Empty;
        }

        var says = lastmod == Sitemaps.SitemapLastmod.UNREADABLE
            ? "not a date"
            : lastmod > DateTimeOffset.MaxValue.ToUnixTimeSeconds() || lastmod < 0
                ? "past the year 9999"
                : DateTimeOffset.FromUnixTimeSeconds(lastmod).UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return total == 1 ? $": {url}, lastmod {says}" : $"; first: {url}, lastmod {says}";
    }

    /// <summary>"In 2 sitemaps: https://x/a.xml and https://x/b.xml", "In 4 sitemaps: https://x/1.xml, https://x/2.xml and 2 more".</summary>
    private static string Repeated(IReadOnlyList<string> sitemaps)
    {
        var shown = sitemaps.Take(LISTED_REPEATS).ToList();
        var more = sitemaps.Count - shown.Count;
        var names = more > 0 ? $"{string.Join(", ", shown)} and {more:N0} more" : string.Join(" and ", shown);
        return $"In {sitemaps.Count:N0} sitemaps: {names}";
    }

    /// <summary>"https://x/a.xml (62,113 URLs), https://x/b.xml (51.2 MB) and 2 more"; null for none.</summary>
    private static string? Files(IEnumerable<StoredSitemap> files, int entries, long bytes)
    {
        var listed = files.ToList();
        return listed.Count == 0
            ? null
            : string.Join(", ", listed.Take(LISTED_FILES).Select(file => $"{file.Url} ({SizeWords(file, entries, bytes)})")) +
              (listed.Count > LISTED_FILES ? $" and {listed.Count - LISTED_FILES:N0} more" : string.Empty);
    }

    /// <summary>What a file has more of than the given counts: "62,113 URLs", "51,000 sitemaps", "51.2 MB"; null for none.</summary>
    private static string? SizeWords(StoredSitemap file, int entries, long bytes)
    {
        List<string> over = [];
        if (file.UrlCount > entries)
        {
            over.Add(RuleText.Count(file.UrlCount, "URL", "URLs"));
        }

        if (file.ListedSitemaps > entries)
        {
            over.Add(RuleText.Count(file.ListedSitemaps.Value, "sitemap", "sitemaps"));
        }

        if (file.SizeBytes > bytes)
        {
            over.Add(RuleText.Size(file.SizeBytes.Value));
        }

        return over.Count == 0 ? null : string.Join(", ", over);
    }

    /// <summary>"https://x/a.xml", "https://x/a.xml and 2 more".</summary>
    private static string Listed(IReadOnlyList<string> files) =>
        files.Count == 1 ? files[0] : $"{files[0]} and {files.Count - 1:N0} more";
}
