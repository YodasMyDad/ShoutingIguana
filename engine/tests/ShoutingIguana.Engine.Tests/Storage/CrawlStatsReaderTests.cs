using System.Globalization;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The crawl's headline numbers against plain SQL, a statement a number, as they were first read: whatever state the
/// crawl is in (analysed, with the link summary; continued since, so the links themselves are read; holding values this
/// engine doesn't know), the passes count what the statements count. They're kept while the crawl stands still, and a
/// read given up stops.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class CrawlStatsReaderTests(GridCrawl crawl) : IDisposable
{
    /// <summary>A page, by plain SQL: Cloudflare's own /cdn-cgi/ endpoints aren't the site's (round 5).</summary>
    private const string PAGE =
        "u.is_internal = 1 AND r.is_html = 1 AND r.outcome = 0 AND r.word_count IS NOT NULL AND instr(lower(u.url), '/cdn-cgi/') = 0";

    private static readonly string NOT_CHECKED =
        $"COALESCE(substr(r.error, 1, {RetryingFetcher.NOT_CHECKED_ERROR.Length}) = '{RetryingFetcher.NOT_CHECKED_ERROR}', 0)";

    public void Dispose() => GridReadContext.ReadWithoutLinkSummary = false;

    [Theory]
    [InlineData("analysed")]
    [InlineData("continued since its analysis")]
    [InlineData("analysed before the link summary")]
    [InlineData("never analysed")]
    [InlineData("values this engine doesn't know")]
    public void EveryNumberIsWhatPlainSqlCounts(string state)
    {
        var path = state switch
        {
            "analysed" or "analysed before the link summary" => crawl.Path,
            "continued since its analysis" => crawl.Changed,
            "never analysed" => NeverAnalysed(),
            _ => Odd()
        };
        GridReadContext.ReadWithoutLinkSummary = state == "analysed before the link summary";

        var stats = CrawlStatsReader.Read(path);

        var expected = Reference(path);
        Assert.Equal(expected.Numbers, Numbers(stats));
        Assert.Equal(expected.Outcomes, Groups(stats.Outcomes.ToDictionary(group => (int)group.Key, group => group.Value)));
        Assert.Equal(expected.StatusCodes, Groups(stats.StatusCodes));
        Assert.Equal(expected.SkipReasons, Groups(stats.SkipReasons.ToDictionary(group => (int)group.Key, group => group.Value)));
        Assert.Equal(expected.NonIndexableReasons, Groups(stats.NonIndexableReasons.ToDictionary(group => (int)group.Key, group => group.Value)));

        // The part that is the crawler's probes of the site's other versions: here its https:// version, which doesn't answer.
        Assert.Equal(Probes(path, "outcome"), Groups(stats.SiteVersions.Outcomes.ToDictionary(group => (int)group.Key, group => group.Value)));
        Assert.Equal(Probes(path, "status_code"), Groups(stats.SiteVersions.StatusCodes));
        Assert.Equal((1L, 1L), (stats.SiteVersions.Fetched, stats.SiteVersions.Outcomes.Values.Sum()));

        // The synthetic site plants what makes each number worth comparing: orphans whose only link is their own canonical,
        // pages only a sitemap led to that are linked, URLs that can't be indexed for every reason, and skipped URLs.
        Assert.Equal(crawl.Server.Site.GetExpectations().Orphans, stats.UnlinkedPageCount);
        Assert.True(TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls u JOIN responses r ON r.url_id = u.id WHERE {PAGE} AND u.source = 2") > stats.UnlinkedPageCount);
        Assert.True(stats.LinkCount > 0 && stats.NonIndexableReasons.Count > 3 && stats.SkipReasons.Count > 0 && stats.IndexablePageCount < stats.HtmlPageCount);
    }

    [Fact]
    public void TheNumbersAreKeptUntilTheCrawlChanges()
    {
        var path = crawl.Copy();
        var cache = new ReadCache();
        var first = CrawlStatsReader.Read(path, cache);

        // Changed behind the crawl's back, its stamp as it was: the numbers kept are the ones given, and a read without
        // the cache counts again.
        Change(path, "DELETE FROM urls WHERE id = (SELECT MAX(id) FROM urls)");
        Assert.Equal(first.UrlCount, CrawlStatsReader.Read(path, cache).UrlCount);
        Assert.Equal(first.UrlCount - 1, CrawlStatsReader.Read(path).UrlCount);

        // A run of the crawl stamps it again: counted again.
        Stamp(path);
        Assert.Equal(first.UrlCount - 1, CrawlStatsReader.Read(path, cache).UrlCount);
    }

    [Fact]
    public void AReadGivenUpStops()
    {
        using var gone = new CancellationTokenSource();
        gone.Cancel();
        Assert.Throws<OperationCanceledException>(() => CrawlStatsReader.Read(crawl.Path, new ReadCache(), gone.Token));
    }

    private string NeverAnalysed()
    {
        var path = crawl.Copy();
        Change(path, "DELETE FROM analysis; DROP TABLE effective_indexability; DROP TABLE link_summary_state; DROP TABLE link_summary; DROP TABLE link_blocks");
        return path;
    }

    /// <summary>
    /// A copy holding what only another program writes: an outcome, a status, a reason and a skip reason no engine has, a
    /// response whose URL has gone, and the analysis judging a URL by a reason of its own.
    /// </summary>
    private string Odd()
    {
        var path = crawl.Copy();
        Change(path, """
            UPDATE responses SET outcome = 42 WHERE url_id = (SELECT MIN(url_id) FROM responses WHERE outcome = 2);
            UPDATE responses SET status_code = 999 WHERE url_id = (SELECT MIN(url_id) FROM responses WHERE status_code = 301);
            UPDATE responses SET status_code = -7 WHERE url_id = (SELECT MAX(url_id) FROM responses WHERE status_code = 301);
            UPDATE responses SET indexability_reason = 77 WHERE url_id = (SELECT MIN(url_id) FROM responses WHERE indexability_reason = 1);
            UPDATE urls SET skip_reason = 55 WHERE id = (SELECT MIN(id) FROM urls WHERE state = 3);
            UPDATE urls SET is_internal = 3 WHERE id = (SELECT MAX(id) FROM urls WHERE is_internal = 1 AND source = 1);
            DELETE FROM urls WHERE id = (SELECT MAX(u.id) FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.is_internal = 0);
            INSERT OR REPLACE INTO effective_indexability (url_id, indexability_reason)
                SELECT MAX(url_id), 88 FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.is_internal = 1 AND u.source = 1 AND r.indexability_reason = 0;
            """);
        return path;
    }

    private static List<string> Probes(string path, string column) =>
        TestCrawls.Rows(path, $"SELECT r.{column}, COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.source = 3 AND r.{column} IS NOT NULL GROUP BY 1");

    private static List<string> Numbers(CrawlStats stats) =>
    [
        $"urls {stats.UrlCount}", $"internal {stats.InternalUrlCount}", $"queued {stats.QueuedCount}", $"links {stats.LinkCount}",
        $"sitemaps {stats.SitemapCount}", $"sitemap errors {stats.SitemapErrorCount}", $"sitemap urls {stats.SitemapUrlCount}",
        $"unlinked {stats.UnlinkedPageCount}", $"pages {stats.HtmlPageCount}", $"indexable {stats.IndexablePageCount}",
        $"average ms {stats.AverageResponseMs.ToString("R", CultureInfo.InvariantCulture)}", $"bytes {stats.TotalBytes}", $"not checked {stats.NotCheckedCount}"
    ];

    private static List<string> Groups(IReadOnlyDictionary<int, long> groups) =>
        [.. groups.OrderBy(group => group.Key).Select(group => $"{group.Key} | {group.Value}")];

    /// <summary>Each number by a statement of its own, as plain as it can be written.</summary>
    private static (List<string> Numbers, List<string> Outcomes, List<string> StatusCodes, List<string> SkipReasons, List<string> NonIndexableReasons) Reference(string path)
    {
        var analysed = TestCrawls.Count(path, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'effective_indexability'") > 0;
        var join = analysed ? "LEFT JOIN effective_indexability e ON e.url_id = r.url_id" : string.Empty;
        var reason = analysed ? "COALESCE(e.indexability_reason, r.indexability_reason)" : "r.indexability_reason";
        var pages = $"FROM responses r JOIN urls u ON u.id = r.url_id {join} WHERE {PAGE}";
        long Count(string sql) => TestCrawls.Count(path, sql);
        List<string> Grouped(string sql) => [.. TestCrawls.Rows(path, sql).OrderBy(row => long.Parse(row.Split(" | ")[0], CultureInfo.InvariantCulture))];

        var average = TestCrawls.Rows(path, $"SELECT COALESCE(AVG(r.response_ms), 0) {pages}").Single();
        return (
            [
                $"urls {Count("SELECT COUNT(*) FROM urls")}", $"internal {Count("SELECT COALESCE(SUM(is_internal), 0) FROM urls")}",
                $"queued {Count("SELECT COUNT(*) FROM urls WHERE state = 0")}", $"links {Count("SELECT COUNT(*) FROM links")}",
                $"sitemaps {Count("SELECT COUNT(*) FROM sitemaps WHERE error IS NULL")}",
                $"sitemap errors {Count("SELECT COUNT(*) FROM sitemaps WHERE error IS NOT NULL AND is_probe = 0 AND error NOT LIKE 'Not read%'")}",
                $"sitemap urls {Count("SELECT COUNT(DISTINCT url_id) FROM sitemap_urls")}",
                $"unlinked {Count($"SELECT COUNT(*) {pages} AND u.source = 2 AND NOT EXISTS (SELECT 1 FROM links l WHERE l.to_id = u.id AND l.from_id <> u.id)")}",
                $"pages {Count($"SELECT COUNT(*) {pages}")}", $"indexable {Count($"SELECT COUNT(*) {pages} AND {reason} = 0")}",
                $"average ms {double.Parse(average, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)}",
                $"bytes {Count($"SELECT COALESCE(SUM(r.size_bytes), 0) {pages}")}", $"not checked {Count($"SELECT COUNT(*) FROM responses r WHERE {NOT_CHECKED}")}"
            ],
            Grouped("SELECT outcome, COUNT(*) FROM responses GROUP BY outcome"),
            Grouped("SELECT status_code, COUNT(*) FROM responses WHERE status_code IS NOT NULL GROUP BY status_code"),
            Grouped("SELECT skip_reason, COUNT(*) FROM urls WHERE state = 3 GROUP BY skip_reason"),
            Grouped($"""
                SELECT {reason}, COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id {join}
                WHERE u.is_internal = 1 AND {reason} <> 0 AND u.source <> {(int)DiscoverySource.SiteVersion} AND NOT {NOT_CHECKED} GROUP BY 1
                """));
    }
}
