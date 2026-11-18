using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Library.Models;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// Which crawl a crawl is compared with by default (the fix loop), and what changed since, from the library's cached
/// summaries alone: no file is opened. A crawl's site is its <see cref="SiteKeyOf">site key</see>, the app's own grouping,
/// so the comparison and the screen that folds a site's crawls together can't disagree.
/// </summary>
internal static class PreviousCrawls
{
    /// <summary>
    /// The site as the library groups it: the start URL's host without a leading www., then <c>:port</c> when the start URL
    /// has a port of its own (not its scheme's). The scheme doesn't count: http and https crawls of a site are one site.
    /// Null for a crawl with no start URL that reads as one.
    /// </summary>
    public static string? SiteKeyOf(string? startUrl)
    {
        if (startUrl is null || !Uri.TryCreate(startUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        var site = host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        return uri.IsDefaultPort ? site : $"{site}:{uri.Port}";
    }

    /// <summary>Each crawl listed, with its previous crawl and its changes since.</summary>
    public static IEnumerable<CrawlListItem> Fill(IReadOnlyList<LibraryEntry> library) => library.Select(entry => With(entry, library));

    /// <summary>A crawl's item with its previous crawl among <paramref name="library"/>, and its changes since.</summary>
    public static CrawlListItem With(LibraryEntry entry, IReadOnlyList<LibraryEntry> library)
    {
        // A crawl never analysed has nothing to compare: no previous crawl either.
        if (Choose(entry, library) is not { Summary: { } then } previous || entry.Summary is not { } now || CrawlComparability.FromSummaries(then, now) is not { } comparison)
        {
            return entry.Item;
        }

        return entry.Item with
        {
            Previous = new PreviousCrawlDto(previous.Item.Id, previous.Item.StartedAt, previous.Item.Score, comparison.Comparable, comparison.PreviousNeedsAnalysis.Count > 0),

            // A crawl's own analysis no longer describes it while it's crawled, or after it was cut short.
            Changes = entry.Item.Status is CrawlListStatus.Running or CrawlListStatus.Paused or CrawlListStatus.Stopping or CrawlListStatus.Interrupted
                or CrawlListStatus.InUse or CrawlListStatus.Unreadable
                ? null
                : new CrawlChangesDto(
                    comparison.ScoreChange,
                    comparison.Partial,
                    Of(CheckChangeState.Fixed),
                    Of(CheckChangeState.New),
                    Of(CheckChangeState.Fewer),
                    Of(CheckChangeState.More),
                    Of(CheckChangeState.NewCheck),
                    Of(CheckChangeState.NotCheckedNow))
        };

        IReadOnlyList<string> Of(CheckChangeState state) => [.. comparison.Checks.Where(check => check.State == state).Select(check => check.CheckId)];
    }

    /// <summary>
    /// The crawl <paramref name="current"/> is compared with by default: the newest of its site's crawls that started before
    /// it and can be compared (<see cref="IsCandidate"/>), one with the same start URL first. Null for a site's first crawl.
    /// </summary>
    public static LibraryEntry? Choose(LibraryEntry current, IEnumerable<LibraryEntry> library)
    {
        if (current.Item.SiteKey is not { } site || current.Item.StartedAt is null)
        {
            return null;
        }

        List<LibraryEntry> candidates =
        [
            .. library.Where(other => other.Item.Id != current.Item.Id && other.Item.SiteKey == site && IsBefore(other.Item, current.Item) && IsCandidate(other))
                .OrderByDescending(other => other.Item.StartedAt)
                .ThenBy(other => other.Item.Id, StringComparer.Ordinal)
        ];
        return candidates.FirstOrDefault(other => string.Equals(other.Item.StartUrl, current.Item.StartUrl, StringComparison.Ordinal)) ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// A crawl another can be compared with: one whose analysis read a page, that isn't live here, in use elsewhere,
    /// interrupted (it can't be analysed), missing or unreadable.
    /// </summary>
    public static bool IsCandidate(LibraryEntry entry) =>
        entry is { Summary: { Analysis.PagesAnalyzed: > 0, CheckCounts: not null }, Item: { Missing: false, Status: CrawlListStatus.Completed or CrawlListStatus.Stopped or CrawlListStatus.Failed } };

    /// <summary>
    /// <paramref name="crawl"/> started before <paramref name="other"/>, in the order Home lists a site's crawls: by when
    /// they started, and two that started at the same moment by id, the one with the smaller id the newer.
    /// </summary>
    private static bool IsBefore(CrawlListItem crawl, CrawlListItem other) =>
        crawl.StartedAt is { } started && (started < other.StartedAt || started == other.StartedAt && string.CompareOrdinal(crawl.Id, other.Id) > 0);
}
