using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>What a crawl file holds, through the engine's readers. Readers are safe at any time, a running crawl included.</summary>
internal sealed class CrawlData(CrawlManager crawls)
{
    private const int DEFAULT_LIMIT = 200;
    private const int MAX_LIMIT = 1_000;

    /// <summary>As the crawl started: header names with empty values, and the proxy without its credentials.</summary>
    public CrawlOptionsDto StoredOptions(string id)
    {
        var path = crawls.PathOf(id);
        return CrawlOptionsDto.From(EngineErrors.Read(() => CrawlDatabase.ReadOptions(path)));
    }

    /// <summary>
    /// The last analysis, whether it still stands, and every check's count or why it was skipped, all from one snapshot of
    /// the file, even if another analysis finishes or the crawl is resumed meanwhile. An analysis by an engine from before
    /// the summary is out of date (the engine changed), with no reasons for its skips: its notes say.
    /// </summary>
    public AnalysisDto Analysis(string id)
    {
        var path = crawls.PathOf(id);
        var results = EngineErrors.Read(() => IssueReader.ReadResults(path)) ?? throw Problems.NotAnalyzed();
        var (analysis, counts, notes) = (results.Analysis, results.Counts, results.Notes);
        var reasons = results.Skipped.ToDictionary(skip => skip.CheckId, StringComparer.Ordinal);
        var outOfDate = results.OutOfDate(crawls.AnalysisSettings);

        // In catalog order. A check from a newer engine, which this one doesn't know, still counts, after the rest.
        List<CheckCountDto> ran = [.. IssueCatalog.All.Where(check => counts.ContainsKey(check.Id)).Select(check => new CheckCountDto(check.Id, counts[check.Id]))];
        ran.AddRange(counts.Keys.Where(checkId => IssueCatalog.Find(checkId) is null).Order(StringComparer.Ordinal)
            .Select(checkId => new CheckCountDto(checkId, counts[checkId])));

        return new AnalysisDto(
            analysis.AnalyzedAt.UtcDateTime,
            analysis.EngineVersion,
            analysis.PagesAnalyzed,
            (long)analysis.Duration.TotalMilliseconds,
            analysis.IsPartial,
            outOfDate.Count > 0,
            outOfDate,
            notes,
            ran,
            [
                .. IssueCatalog.All.Where(check => !counts.ContainsKey(check.Id)).Select(check => new SkippedCheckDto(check.Id,
                    reasons.TryGetValue(check.Id, out var skip) ? new SkipReasonDto(skip.Reason, skip.Text) : null))
            ]);
    }

    /// <summary>One page of the URLs a check flagged, in crawl order. A check the analysis skipped has no total, not 0.</summary>
    public IssuesPageDto Issues(string id, string checkId, IQueryCollection query)
    {
        var path = crawls.PathOf(id);
        var limit = Number(query, "limit", DEFAULT_LIMIT, 1, MAX_LIMIT, $"limit must be a whole number from 1 to {MAX_LIMIT:N0}.");
        var offset = Number(query, "offset", 0, 0, int.MaxValue, "offset must be a whole number, 0 or more.");
        var page = EngineErrors.Read(() => IssueReader.ReadIssuePage(path, checkId, limit, offset)) ?? throw Problems.NotAnalyzed();
        if (page.Total is not { } total)
        {
            return IssueCatalog.Find(checkId) is null ? throw Problems.CheckNotFound(checkId) : new IssuesPageDto([], null);
        }

        return new IssuesPageDto([.. page.Rows.Select(row => new IssueRowDto(row.Url, row.Detail, row.RelatedUrl))], total);
    }

    private static int Number(IQueryCollection query, string name, int fallback, int min, int max, string message)
    {
        var values = query[name];
        if (values.Count == 0)
        {
            return fallback;
        }

        return values.Count == 1 && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
            ? number
            : throw Problems.BadRequest(message);
    }
}
