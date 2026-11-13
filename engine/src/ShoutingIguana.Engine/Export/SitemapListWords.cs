using System.Globalization;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>
/// The Sitemaps screen's words for its list, as its cells show them (the app's <c>screens/sitemaps/sitemapWords.ts</c>,
/// <c>sitemapTree.ts</c> and <c>sitemapLinks.ts</c>): how a sitemap was found, what became of it, and its URLs counted,
/// an index's only once the crawl has read every sitemap under it. An export of the list writes these, so the file
/// reads as the screen does.
/// </summary>
internal static partial class SitemapListWords
{
    /// <summary>The counts the screens show, under their titles, the list's five first.</summary>
    public static readonly string[] COUNT_TITLES = ["Listed", "Crawled", "Blocked by robots.txt", "Not crawled", "Indexable", "Non-indexable", "Redirecting or broken"];

    /// <summary>How the crawl came by a sitemap, as the list's "Found via" says it.</summary>
    public static string FoundVia(SitemapSource? foundVia) => foundVia switch
    {
        SitemapSource.RobotsTxt => "robots.txt",
        SitemapSource.Options => "Crawl settings",
        SitemapSource.DefaultLocation => "Default location",
        SitemapSource.Index => "Sitemap index",
        _ => "Not known"
    };

    /// <summary>The sitemaps under one, as <c>subtreeOf</c> counts them.</summary>
    public static SitemapSubtree SubtreeOf(IReadOnlyList<SitemapInfo> sitemaps, int id, SitemapDiscovery discovery)
    {
        List<SitemapInfo> under = [];
        HashSet<int> seen = [id];
        Stack<int> pending = new([id]);
        while (pending.TryPop(out var parent))
        {
            foreach (var sitemap in sitemaps.Where(sitemap => sitemap.ParentId == parent && seen.Add(sitemap.Id)))
            {
                under.Add(sitemap);
                pending.Push(sitemap.Id);
            }
        }

        var more = discovery is not (SitemapDiscovery.Done or SitemapDiscovery.Off) || under.Any(sitemap => sitemap.State == SitemapState.NotRead);
        return new SitemapSubtree(under.Count, under.Count(OwnCountsKnown), more);
    }

    /// <summary>
    /// Whether the crawl knows what a sitemap lists: one left unread, not read yet, or failed before an entry was read
    /// lists an unknown number; an index's counts are known only once every sitemap under it is.
    /// </summary>
    public static bool CountsKnown(SitemapInfo sitemap, SitemapSubtree subtree) =>
        OwnCountsKnown(sitemap) && (!(sitemap.IsIndex || subtree.Sitemaps > 0) || subtree.Whole);

    /// <summary>A sitemap's counts as the screens show them, in <see cref="COUNT_TITLES"/>' order; null where they aren't known.</summary>
    public static long[]? Counts(SitemapInfo sitemap, SitemapSubtree subtree)
    {
        if (!CountsKnown(sitemap, subtree))
        {
            return null;
        }

        var urls = sitemap.Urls;
        return
        [
            urls.Listed, Math.Max(0, urls.Fetched - urls.RobotsDisallowed), urls.RobotsDisallowed, Math.Max(0, urls.Listed - urls.Fetched), urls.Indexable,
            urls.NonIndexable, urls.RedirectingOrBroken
        ];
    }

    /// <summary>What became of a sitemap, as the list's "Status" cell says it.</summary>
    /// <param name="crawling">The crawl is running: a sitemap it hasn't read is still on its way.</param>
    public static string State(SitemapInfo sitemap, bool crawling, SitemapSubtree subtree)
    {
        // An index that was read says so of itself only: whether the sitemaps it lists were read is another matter.
        if (sitemap.State == SitemapState.Read && (sitemap.IsIndex || subtree.Sitemaps > 0) && !subtree.Whole)
        {
            return PartIndex(subtree, crawling);
        }

        switch (sitemap.State)
        {
            case SitemapState.Read:
                return "Read";
            case SitemapState.Failed:
                var failure = Failure(sitemap.Error);
                var why = failure is { Words.Length: > 0 } known ? $": {known.Words}" : string.Empty;
                if (sitemap is { FoundVia: SitemapSource.DefaultLocation, StatusCode: 404 or 410 })
                {
                    return $"None there{why}";
                }

                return sitemap.Entries > 0 || sitemap.Urls.Listed > 0 ? $"Read in part{why}" : $"Couldn’t be read{why}";
            case SitemapState.NotRead:
                return "Left unread";
            default:
                return crawling ? "Not read yet" : "Never read";
        }
    }

    /// <summary>Why a sitemap couldn't be read: a failure in plain words, and a file whose XML is broken by the line the parser stopped at.</summary>
    private static PlainFailure? Failure(string? error)
    {
        if (FailureWords.Of(error) is not { IsOther: true } other)
        {
            return FailureWords.Of(error);
        }

        var line = BrokenAt().Match(error!);
        return other with { Words = line.Success ? $"its XML is broken at line {Count(long.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture))}" : string.Empty };
    }

    /// <summary>An index the crawl read, without every sitemap under it read.</summary>
    private static string PartIndex(SitemapSubtree subtree, bool crawling)
    {
        var (has, read) = (subtree.Sitemaps, subtree.Read);
        var readOf = read == 0 ? "none" : Count(read);
        if (!subtree.More)
        {
            return $"Read, {readOf} of its {CountOf(has, "sitemap")}";
        }

        if (has == 0)
        {
            return crawling ? "Read, its sitemaps to come" : "Read, none of its sitemaps";
        }

        return crawling ? $"Read, {CountOf(has, "sitemap")} so far" : $"Read, has {Count(has)} of its sitemaps";
    }

    private static bool OwnCountsKnown(SitemapInfo sitemap) => sitemap.State switch
    {
        SitemapState.Read => true,
        SitemapState.Failed => sitemap.Entries > 0 || sitemap.Urls.Listed > 0,
        _ => false
    };

    private static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    private static string CountOf(long count, string noun) => $"{Count(count)} {noun}{(count == 1 ? string.Empty : "s")}";

    [GeneratedRegex(@"\bLine (\d+), position \d+", RegexOptions.IgnoreCase)]
    private static partial Regex BrokenAt();
}
