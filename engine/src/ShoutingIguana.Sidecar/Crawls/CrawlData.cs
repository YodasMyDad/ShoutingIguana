using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Library;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>What a crawl file holds, through the engine's readers. Readers are safe at any time, a running crawl included.</summary>
internal sealed class CrawlData(CrawlManager crawls, CrawlSummaryCache summaries)
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
    /// The last analysis, whether it still stands, and every check's count or why it was skipped. An analysis by an
    /// engine from before the summary is out of date (the engine changed), with no reasons for its skips: its notes say.
    /// </summary>
    public AnalysisDto Analysis(string id)
    {
        var path = crawls.PathOf(id);

        // The library's summary of the file, read once per change of it: the analysis as stored, and the crawl's stamp.
        var cached = summaries.Get(id, path);
        var crawl = cached.Summary ?? throw EngineErrors.ForRead(cached.Error ?? new FileNotFoundException("Crawl database not found.", path));
        var analysis = crawl.Analysis ?? throw Problems.NotAnalyzed();
        var counts = EngineErrors.Read(() => IssueReader.ReadCounts(path));
        var notes = EngineErrors.Read(() => IssueReader.ReadNotes(path));
        var reasons = EngineErrors.Read(() => IssueReader.ReadSkippedChecks(path)).ToDictionary(skip => skip.CheckId, StringComparer.Ordinal);
        var outOfDate = crawl.OutOfDate(crawls.AnalysisSettings);

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

    public CrawlStatsDto Stats(string id)
    {
        var path = crawls.PathOf(id);
        return CrawlStatsDto.From(EngineErrors.Read(() => CrawlStatsReader.Read(path)));
    }

    /// <summary>One page of the URLs a check flagged, in crawl order. A check the analysis skipped has no total, not 0.</summary>
    public IssuesPageDto Issues(string id, string checkId, IQueryCollection query)
    {
        var path = crawls.PathOf(id);
        var limit = Number(query, "limit", DEFAULT_LIMIT, 1, MAX_LIMIT, $"limit must be a whole number from 1 to {MAX_LIMIT:N0}.");
        var offset = Number(query, "offset", 0, 0, int.MaxValue, "offset must be a whole number, 0 or more.");
        if (EngineErrors.Read(() => IssueReader.ReadAnalysis(path)) is null)
        {
            throw Problems.NotAnalyzed();
        }

        var counts = EngineErrors.Read(() => IssueReader.ReadCounts(path));
        if (!counts.TryGetValue(checkId, out var total))
        {
            return IssueCatalog.Find(checkId) is null ? throw Problems.CheckNotFound(checkId) : new IssuesPageDto([], null);
        }

        var rows = EngineErrors.Read(() => IssueReader.ReadIssues(path, checkId, limit, offset));
        return new IssuesPageDto([.. rows.Select(row => new IssueRowDto(row.Url, row.Detail, row.RelatedUrl))], total);
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
