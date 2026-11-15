using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// Checks an analysed crawl's URL summary against the crawl's own tables and against what the checks flagged: the grid
/// must show what the analysis judged, so the two can never disagree. Each number is worked out here from the stored
/// rows by other means than the summary uses (SQL where it uses the analysis's table in memory).
/// </summary>
internal static class UrlSummaryAgreement
{
    /// <summary>Indexable-only checks: content checks look only at the pages search engines can index.</summary>
    private static readonly IssueDefinition[] INDEXABLE_ONLY =
    [
        IssueCatalog.TITLE_MISSING, IssueCatalog.TITLE_MULTIPLE, IssueCatalog.TITLE_TOO_LONG, IssueCatalog.TITLE_TOO_SHORT, IssueCatalog.TITLE_DUPLICATE,
        IssueCatalog.DESCRIPTION_MISSING, IssueCatalog.DESCRIPTION_MULTIPLE, IssueCatalog.DESCRIPTION_TOO_LONG, IssueCatalog.DESCRIPTION_TOO_SHORT,
        IssueCatalog.DESCRIPTION_DUPLICATE, IssueCatalog.H1_MISSING, IssueCatalog.H1_MULTIPLE, IssueCatalog.H1_DUPLICATE, IssueCatalog.LOW_WORD_COUNT,
        IssueCatalog.SOFT_404, IssueCatalog.DUPLICATE_CONTENT, IssueCatalog.NEAR_DUPLICATE_CONTENT, IssueCatalog.URL_UPPERCASE, IssueCatalog.URL_UNDERSCORES,
        IssueCatalog.URL_TOO_LONG, IssueCatalog.OPEN_GRAPH_MISSING, IssueCatalog.SNIPPET_BLOCKED, IssueCatalog.CANONICAL_MISSING, IssueCatalog.NOT_IN_SITEMAP,
        IssueCatalog.ORPHAN_PAGE
    ];

    /// <summary>The pages the analysis audits, as its reader picks them: responses r of urls u.</summary>
    private const string AUDITED = $"u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND {AnalysisReader.SITE_PAGE}";

    /// <summary>A URL the analysis counts as fetched: it has a response, and one from a request (responses r).</summary>
    private static readonly string FETCHED =
        $"r.url_id IS NOT NULL AND substr(COALESCE(r.error, ''), 1, {RetryingFetcher.NOT_CHECKED_ERROR.Length}) <> '{RetryingFetcher.NOT_CHECKED_ERROR}'";

    public static void AssertAgreesWithTheChecks(string path, SyntheticSiteExpectations expected, CrawlStats stats)
    {
        Assert.True(UrlSummaryReader.IsAvailable(path));
        var rows = UrlSummaryReader.ReadAll(path).ToDictionary(row => row.UrlId);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"), rows.Count);

        AssertIndexabilityAsAnalysed(path, rows);
        AssertTheChecksSawTheSameIndexability(path, rows);

        // What the site planted, counted from the summary: the stats count it from the crawl's tables. Both leave out the
        // crawler's probes of the site's other versions, and the URLs that weren't checked are unknown, not unreachable.
        var internalRows = rows.Values.Where(row => row.IsInternal && row.Source != DiscoverySource.SiteVersion).ToList();
        Assert.Equal(expected.Noindex, internalRows.Count(row => row.IndexabilityReason == IndexabilityReason.Noindex));
        Assert.Equal(expected.Canonicalised, internalRows.Count(row => row.IndexabilityReason == IndexabilityReason.Canonicalised));
        Assert.Equal(stats.NonIndexableReasons.Where(pair => pair.Key != IndexabilityReason.None).OrderBy(pair => pair.Key),
            internalRows.Where(row => row.IndexabilityReason is not (null or IndexabilityReason.None)).GroupBy(row => row.IndexabilityReason!.Value)
                .Select(group => KeyValuePair.Create(group.Key, (long)group.Count())).OrderBy(pair => pair.Key));
        // Cloudflare's own endpoints are no page of the site's in any count of pages (CrawlSchema.PAGE), though the summary
        // types them as pages.
        var cloudflare = TestCrawls.Rows(path, "SELECT id FROM urls WHERE instr(lower(url), '/cdn-cgi/') > 0")
            .Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToHashSet();
        Assert.Equal(stats.IndexablePageCount,
            internalRows.Count(row => row is { IsIndexable: true, ResourceType: ResourceType.Page } && !cloudflare.Contains(row.UrlId)));
        Assert.Equal(expected.NotChecked, stats.NotCheckedCount);

        AssertLinkCounts(path, rows);
        AssertIssueCounts(path, rows);
        AssertRedirectsEndWhereTheyLead(path, rows);
        AssertCopiesOfTheCrawl(path, rows);
    }

    /// <summary>
    /// The UI's rule for indexability, COALESCE(effective_indexability, responses), for every URL the analysis counts as
    /// fetched; nothing for the rest, including URLs never asked for because their host had stopped answering, nor for an
    /// HTML page whose body was never read (an iframe's, another site's) unless its response rules it out.
    /// </summary>
    private static void AssertIndexabilityAsAnalysed(string path, Dictionary<int, UrlSummary> rows)
    {
        var judged = TestCrawls.Rows(path, $"""
            SELECT u.id, CASE WHEN NOT ({FETCHED}) THEN NULL
                              WHEN r.outcome = 0 AND r.is_html = 1 AND r.word_count IS NULL AND COALESCE(e.indexability_reason, r.indexability_reason) = 0 THEN NULL
                              ELSE COALESCE(e.indexability_reason, r.indexability_reason) END
            FROM urls u LEFT JOIN responses r ON r.url_id = u.id LEFT JOIN effective_indexability e ON e.url_id = u.id
            """);
        Assert.Equal(judged, rows.Values.Select(row => $"{row.UrlId} | {(row.IndexabilityReason is { } reason ? ((int)reason).ToString(CultureInfo.InvariantCulture) : "null")}")
            .Order(StringComparer.Ordinal).ToList());
        Assert.All(rows.Values, row => Assert.Equal(row.IndexabilityReason is { } reason ? reason == IndexabilityReason.None : null, row.IsIndexable));

        // The analysis's own corrections are there: an instant meta refresh is a redirect, a canonical leading back isn't one.
        Assert.All(TestCrawls.Rows(path, "SELECT url_id, indexability_reason FROM effective_indexability"), change =>
        {
            var parts = change.Split(" | ");
            Assert.Equal((IndexabilityReason)int.Parse(parts[1], CultureInfo.InvariantCulture), rows[int.Parse(parts[0], CultureInfo.InvariantCulture)].IndexabilityReason);
        });
    }

    private static void AssertTheChecksSawTheSameIndexability(string path, Dictionary<int, UrlSummary> rows)
    {
        var flagged = TestCrawls.Rows(path, "SELECT issue, url_id FROM issues").Select(row => row.Split(" | "))
            .ToLookup(row => row[0], row => rows[int.Parse(row[1], CultureInfo.InvariantCulture)]);
        foreach (var check in INDEXABLE_ONLY)
        {
            Assert.All(flagged[check.Id], row => Assert.True(row.IsIndexable == true, $"{check.Id} flagged URL {row.UrlId}, which the summary says isn't indexable"));
        }

        Assert.All(flagged[IssueCatalog.NOINDEX_PAGE.Id], row => Assert.Equal(IndexabilityReason.Noindex, row.IndexabilityReason));
        Assert.All(flagged[IssueCatalog.CANONICALISED_PAGE.Id], row => Assert.Equal(IndexabilityReason.Canonicalised, row.IndexabilityReason));
        Assert.All(flagged[IssueCatalog.SITEMAP_NON_INDEXABLE.Id], row => Assert.True(row is { InSitemap: true, IsIndexable: false }));
        Assert.All(flagged[IssueCatalog.NOT_IN_SITEMAP.Id], row => Assert.True(row is { InSitemap: false, ResourceType: ResourceType.Page }));
        Assert.All(flagged[IssueCatalog.ORPHAN_PAGE.Id], row => Assert.True(row is { InSitemap: true, LinkingPages: 0 }));

        // The reverse: what the summary calls an indexable page had its HTML read, so a page that was only status-checked
        // never passes for one, and each of the site's was audited by the analysis, apart from Cloudflare's endpoints, which
        // it leaves alone. This site has such an unread page: the one its iframe shows.
        var audited = TestCrawls.Rows(path, $"SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE ({AUDITED}) OR instr(u.url, '/cdn-cgi/') > 0")
            .Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToHashSet();
        Assert.All(rows.Values.Where(row => row is { IsIndexable: true, ResourceType: ResourceType.Page }), row =>
        {
            Assert.NotNull(row.WordCount);
            Assert.True(!row.IsInternal || audited.Contains(row.UrlId), $"URL {row.UrlId} is called indexable, but the analysis didn't audit it");
        });
        Assert.Contains(rows.Values, row => row is { IsInternal: true, StatusCode: 200, ResourceType: ResourceType.Page, IsIndexable: null, Outlinks: null });
    }

    /// <summary>Hyperlinks to each URL from the pages the analysis audits, other than its own, counted by SQL.</summary>
    private static void AssertLinkCounts(string path, Dictionary<int, UrlSummary> rows)
    {
        var counted = TestCrawls.Rows(path, $"""
            SELECT l.to_id, COUNT(*), SUM(r.is_nofollow = 1 OR (l.flags & {(int)LinkFlags.NoFollow}) <> 0), COUNT(DISTINCT l.from_id)
            FROM links l JOIN responses r ON r.url_id = l.from_id JOIN urls u ON u.id = l.from_id
            WHERE l.kind = {(int)LinkKind.Hyperlink} AND l.from_id <> l.to_id AND {AUDITED}
            GROUP BY l.to_id
            """);
        Assert.Equal(counted, rows.Values.Where(row => row.Inlinks > 0)
            .Select(row => $"{row.UrlId} | {row.Inlinks} | {row.NofollowInlinks} | {row.LinkingPages}").Order(StringComparer.Ordinal).ToList());
        Assert.All(rows.Values, row => Assert.Equal(row.Inlinks, row.FollowedInlinks + row.NofollowInlinks));
        Assert.Contains(rows.Values, row => row.NofollowInlinks > 0);
        Assert.Contains(rows.Values, row => row.Inlinks > row.LinkingPages);

        // A page's links are known once its HTML was read; a URL only status-checked has none to count.
        Assert.Equal(TestCrawls.Rows(path, $"""
                SELECT u.id, CASE WHEN NOT ({FETCHED}) OR r.word_count IS NULL THEN NULL ELSE r.outlink_count END
                FROM urls u LEFT JOIN responses r ON r.url_id = u.id
                """),
            rows.Values.Select(row => $"{row.UrlId} | {Text(row.Outlinks)}").Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>The checks that flagged each URL, by the catalog's severity.</summary>
    private static void AssertIssueCounts(string path, Dictionary<int, UrlSummary> rows)
    {
        var flagged = TestCrawls.Rows(path, "SELECT url_id, issue FROM issues").Select(row => row.Split(" | "))
            .GroupBy(row => int.Parse(row[0], CultureInfo.InvariantCulture), row => IssueCatalog.Find(row[1])!.Severity)
            .ToDictionary(group => group.Key, group => group.ToList());
        Assert.All(rows.Values, row =>
        {
            var severities = flagged.GetValueOrDefault(row.UrlId) ?? [];
            Assert.Equal(
                (severities.Count, severities.Count(severity => severity == IssueSeverity.Error), severities.Count(severity => severity == IssueSeverity.Warning),
                    severities.Count(severity => severity == IssueSeverity.Notice)),
                (row.Issues, row.Errors, row.Warnings, row.Notices));
        });
        Assert.Contains(rows.Values, row => row.Issues > 1);
    }

    /// <summary>
    /// HTTP redirects followed hop by hop through the responses: where a chain ends at a URL that isn't one, the summary
    /// ends there too, with its status. Instant meta refreshes are the analysis's redirects too, ending past the page.
    /// </summary>
    private static void AssertRedirectsEndWhereTheyLead(string path, Dictionary<int, UrlSummary> rows)
    {
        var redirects = TestCrawls.Rows(path, $"SELECT url_id, redirect_url_id FROM responses WHERE outcome = {(int)FetchOutcome.Redirect} AND redirect_url_id IS NOT NULL")
            .Select(row => row.Split(" | ")).ToDictionary(row => int.Parse(row[0], CultureInfo.InvariantCulture), row => int.Parse(row[1], CultureInfo.InvariantCulture));
        var refreshes = TestCrawls.Rows(path, $"SELECT url_id FROM effective_indexability WHERE indexability_reason = {(int)IndexabilityReason.Redirected}")
            .Select(row => int.Parse(row, CultureInfo.InvariantCulture)).ToHashSet();
        var traced = 0;
        foreach (var (start, first) in redirects)
        {
            List<int> seen = [start];
            var current = first;
            while (redirects.TryGetValue(current, out var next) && !seen.Contains(current) && seen.Count < UrlTable.MAX_REDIRECT_HOPS)
            {
                seen.Add(current);
                current = next;
            }

            if (redirects.ContainsKey(current) || refreshes.Contains(current))
            {
                continue;
            }

            traced++;
            Assert.Equal((current, rows[current].StatusCode), (rows[start].FinalUrlId, rows[start].FinalStatusCode));
        }

        Assert.True(traced > 0);
        Assert.All(refreshes, page => Assert.NotEqual(page, rows[page].FinalUrlId));
        Assert.All(rows.Values.Where(row => !redirects.ContainsKey(row.UrlId) && !refreshes.Contains(row.UrlId)),
            row => Assert.Equal((row.UrlId, row.StatusCode), (row.FinalUrlId, row.FinalStatusCode)));
    }

    /// <summary>
    /// The crawl's own numbers, copied for the grids to sort on (unknown for a URL the analysis counts as unfetched), how
    /// each URL was found, its depth (none for one only a sitemap lists), and whether a sitemap lists it: no only when the
    /// analysis could tell, having read every sitemap.
    /// </summary>
    private static void AssertCopiesOfTheCrawl(string path, Dictionary<int, UrlSummary> rows)
    {
        Assert.Equal(TestCrawls.Rows(path, $"""
                SELECT id, is_internal, source, CASE WHEN depth < {CrawlDepth.UNLINKED} THEN depth END,
                       CASE WHEN EXISTS (SELECT 1 FROM sitemap_urls s WHERE s.url_id = urls.id) THEN 1
                            WHEN EXISTS (SELECT 1 FROM issue_types WHERE id = '{IssueCatalog.NOT_IN_SITEMAP.Id}') THEN 0 END
                FROM urls
                """),
            rows.Values.Select(row => $"{row.UrlId} | {(row.IsInternal ? 1 : 0)} | {(int)row.Source} | {Text(row.Depth)} | {Text(row.InSitemap is { } listed ? listed ? 1 : 0 : (int?)null)}")
                .Order(StringComparer.Ordinal).ToList());
        Assert.Equal(TestCrawls.Rows(path, $"""
                SELECT u.id, CASE WHEN {FETCHED} THEN r.status_code END, CASE WHEN {FETCHED} THEN r.size_bytes END,
                       CASE WHEN {FETCHED} AND r.outcome <> {(int)FetchOutcome.RobotsDisallowed} THEN r.response_ms END, CASE WHEN {FETCHED} THEN r.word_count END
                FROM urls u LEFT JOIN responses r ON r.url_id = u.id
                """),
            rows.Values.Select(row => $"{row.UrlId} | {Text(row.StatusCode)} | {Text(row.SizeBytes)} | {Text(row.ResponseMs)} | {Text(row.WordCount)}")
                .Order(StringComparer.Ordinal).ToList());

        // Every page whose HTML was parsed is a page, and the site's images are images.
        Assert.All(rows.Values.Where(row => row.WordCount is not null), row => Assert.Equal(ResourceType.Page, row.ResourceType));
        Assert.Contains(rows.Values, row => row.ResourceType == ResourceType.Image);
    }

    private static string Text<T>(T? value) where T : struct => value is { } known ? Convert.ToString(known, CultureInfo.InvariantCulture)! : "null";
}
