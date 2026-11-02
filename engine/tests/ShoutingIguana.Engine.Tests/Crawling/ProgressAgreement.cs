using System.Globalization;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A crawl's last progress against what its file stored: the live counts by status class and code, why URLs had no
/// response, and the site's own URLs found and fetched, end where SQL over the responses and URLs puts them. Called after
/// every end-to-end crawl, resumed ones included, so refetches, adopted probes and restored counts are all covered.
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
            InternalFound = counts.InternalFound,
            InternalFetched = counts.InternalFetched
        }, path);
    }

    public static void AssertMatchesTheFile(CrawlProgress progress, string path)
    {
        var blocked = (int)FetchOutcome.Blocked;
        var classes = Grouped(path, $"""
            SELECT CASE WHEN outcome = {blocked} THEN 'blocked'
                        WHEN status_code IS NULL THEN 'none'
                        WHEN status_code BETWEEN 200 AND 299 THEN '2xx'
                        WHEN status_code BETWEEN 300 AND 399 THEN '3xx'
                        WHEN status_code BETWEEN 400 AND 499 THEN '4xx'
                        WHEN status_code BETWEEN 500 AND 599 THEN '5xx'
                        ELSE 'other' END, COUNT(*)
            FROM responses GROUP BY 1
            """);
        Assert.Equal(new CrawlStatusClasses(classes.GetValueOrDefault("2xx"), classes.GetValueOrDefault("3xx"), classes.GetValueOrDefault("4xx"),
            classes.GetValueOrDefault("5xx"), classes.GetValueOrDefault("other"), classes.GetValueOrDefault("none"), classes.GetValueOrDefault("blocked")),
            progress.StatusClasses);
        var sum = progress.StatusClasses;
        Assert.Equal(progress.Fetched, sum.Success + sum.Redirect + sum.ClientError + sum.ServerError + sum.OtherStatus + sum.NoResponse + sum.Blocked);

        // Each class's codes add up to it: a block page's status isn't the site's.
        var codes = Grouped(path, $"SELECT status_code, COUNT(*) FROM responses WHERE status_code IS NOT NULL AND outcome <> {blocked} GROUP BY 1");
        Assert.Equal(codes.OrderBy(code => code.Key, StringComparer.Ordinal),
            progress.StatusCodes.Select(code => KeyValuePair.Create(code.Key.ToString(CultureInfo.InvariantCulture), code.Value)).OrderBy(code => code.Key, StringComparer.Ordinal));

        var reasons = Grouped(path, $"""
            SELECT CASE WHEN outcome = {(int)FetchOutcome.RobotsDisallowed} THEN 'robots'
                        WHEN outcome = {(int)FetchOutcome.Timeout} THEN 'timeout'
                        WHEN outcome = {(int)FetchOutcome.NetworkError} AND {AnalysisReader.NOT_CHECKED} THEN 'not-checked'
                        WHEN outcome = {(int)FetchOutcome.NetworkError} THEN 'network'
                        ELSE 'processing' END, COUNT(*)
            FROM responses r WHERE status_code IS NULL AND outcome <> {blocked} GROUP BY 1
            """);
        Assert.Equal(new CrawlNoResponseCounts(reasons.GetValueOrDefault("robots"), reasons.GetValueOrDefault("timeout"), reasons.GetValueOrDefault("network"),
            reasons.GetValueOrDefault("not-checked"), reasons.GetValueOrDefault("processing")), progress.NoResponse);
        Assert.Equal(progress.RobotsDisallowed, progress.NoResponse.RobotsDisallowed);

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
