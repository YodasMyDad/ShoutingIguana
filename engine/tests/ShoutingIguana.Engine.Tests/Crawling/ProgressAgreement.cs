using System.Globalization;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A crawl's last progress against what its file stored: the live counts by status class and code, why URLs had no
/// response, the part of each that is the probes of the site's other versions, the URLs crawled, and the site's own URLs
/// found and fetched, end where SQL over the responses and URLs puts them. Called after every end-to-end crawl, resumed
/// ones included, so refetches, adopted probes and restored counts are all covered.
/// </summary>
internal static class ProgressAgreement
{
    /// <summary>The counts the writer last published, for tests that drive the writer themselves.</summary>
    public static void AssertMatchesTheFile(CrawlCounters counters, string path)
    {
        var counts = counters.Current;
        AssertMatchesTheFile(new CrawlProgress(CrawlStatus.Running, TimeSpan.Zero, counts.Discovered, counts.Queued, counts.InFlight,
            counts.Fetched, counts.HtmlPages, counts.Redirects, counts.Errors, counts.Blocked, counts.RobotsDisallowed,
            counts.Skipped, counts.Bytes, 0)
        {
            StatusClasses = counts.StatusClasses,
            StatusCodes = counts.StatusCodes,
            NoResponse = counts.NoResponse,
            SiteVersions = counts.SiteVersions,
            InternalFound = counts.InternalFound,
            InternalFetched = counts.InternalFetched
        }, path);
    }

    public static void AssertMatchesTheFile(CrawlProgress progress, string path)
    {
        AssertAnswersMatch(path, "1 = 1", progress.Fetched, progress.StatusClasses, progress.StatusCodes, progress.NoResponse);
        Assert.Equal(progress.RobotsDisallowed, progress.NoResponse.RobotsDisallowed);

        // The URLs crawled are the ones the URL grid has as fetched: every outcome but "not checked".
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses r WHERE NOT {AnalysisReader.NOT_CHECKED}"), progress.Crawled);

        // The part of each count that is the probes of the site's other versions, as the file has them now: a probe the
        // site came to lead to is one of the site's URLs, and not among them.
        var probes = progress.SiteVersions;
        AssertAnswersMatch(path, $"u.source = {(int)DiscoverySource.SiteVersion}", probes.Fetched, probes.StatusClasses, probes.StatusCodes, probes.NoResponse);

        // The site's own URLs: not the probes of its other versions, which no URL limit counts.
        var siteUrl = $"u.is_internal = 1 AND u.source <> {(int)DiscoverySource.SiteVersion}";
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE {siteUrl}"), progress.InternalFetched);
        Assert.Equal(Count(path, $"SELECT COUNT(*) FROM urls u WHERE {siteUrl} AND u.state <> {(int)UrlState.Skipped}"), progress.InternalFound);

        // A crawl that has ended never passed its URL limit, whatever it found on the way: what an ETA heads for.
        if (progress.MaxUrls is { } limit)
        {
            Assert.InRange(progress.InternalFound, progress.InternalFetched, limit);
        }
    }

    /// <summary>The responses of the URLs <paramref name="which"/> selects (SQL over <c>urls u</c>), by class, code and reason.</summary>
    private static void AssertAnswersMatch(string path, string which, long fetched, CrawlStatusClasses statusClasses, IReadOnlyDictionary<int, long> statusCodes,
        CrawlNoResponseCounts noResponse)
    {
        var blocked = (int)FetchOutcome.Blocked;
        var responses = $"FROM responses r JOIN urls u ON u.id = r.url_id WHERE {which}";
        var classes = Grouped(path, $"""
            SELECT CASE WHEN outcome = {blocked} THEN 'blocked'
                        WHEN status_code IS NULL THEN 'none'
                        WHEN status_code BETWEEN 200 AND 299 THEN '2xx'
                        WHEN status_code BETWEEN 300 AND 399 THEN '3xx'
                        WHEN status_code BETWEEN 400 AND 499 THEN '4xx'
                        WHEN status_code BETWEEN 500 AND 599 THEN '5xx'
                        ELSE 'other' END, COUNT(*)
            {responses} GROUP BY 1
            """);
        Assert.Equal(new CrawlStatusClasses(classes.GetValueOrDefault("2xx"), classes.GetValueOrDefault("3xx"), classes.GetValueOrDefault("4xx"),
            classes.GetValueOrDefault("5xx"), classes.GetValueOrDefault("other"), classes.GetValueOrDefault("none"), classes.GetValueOrDefault("blocked")),
            statusClasses);
        var sum = statusClasses;
        Assert.Equal(fetched, sum.Success + sum.Redirect + sum.ClientError + sum.ServerError + sum.OtherStatus + sum.NoResponse + sum.Blocked);
        Assert.Equal(Count(path, $"SELECT COUNT(*) {responses}"), fetched);

        // Each class's codes add up to it: a block page's status isn't the site's.
        var codes = Grouped(path, $"SELECT status_code, COUNT(*) {responses} AND status_code IS NOT NULL AND outcome <> {blocked} GROUP BY 1");
        Assert.Equal(codes.OrderBy(code => code.Key, StringComparer.Ordinal),
            statusCodes.Select(code => KeyValuePair.Create(code.Key.ToString(CultureInfo.InvariantCulture), code.Value)).OrderBy(code => code.Key, StringComparer.Ordinal));

        var reasons = Grouped(path, $"""
            SELECT CASE WHEN outcome = {(int)FetchOutcome.RobotsDisallowed} THEN 'robots'
                        WHEN outcome = {(int)FetchOutcome.Timeout} THEN 'timeout'
                        WHEN outcome = {(int)FetchOutcome.NetworkError} AND {AnalysisReader.NOT_CHECKED} THEN 'not-checked'
                        WHEN outcome = {(int)FetchOutcome.NetworkError} THEN 'network'
                        ELSE 'processing' END, COUNT(*)
            {responses} AND status_code IS NULL AND outcome <> {blocked} GROUP BY 1
            """);
        Assert.Equal(new CrawlNoResponseCounts(reasons.GetValueOrDefault("robots"), reasons.GetValueOrDefault("timeout"), reasons.GetValueOrDefault("network"),
            reasons.GetValueOrDefault("not-checked"), reasons.GetValueOrDefault("processing")), noResponse);
    }

    private static long Count(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return Convert.ToInt64(CrawlDatabase.Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, long> Grouped(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        Dictionary<string, long> groups = new(StringComparer.Ordinal);
        while (reader.Read())
        {
            groups[Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!] = reader.GetInt64(1);
        }

        return groups;
    }
}
