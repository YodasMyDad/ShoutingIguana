using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
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
    /// The thresholds plain words say their limits by (<see cref="PlainLimits"/>): those the named crawl's last analysis
    /// ran with, so its words say what that crawl was judged by; or, when no crawl is named, the crawl was never analysed
    /// or its settings can't be read, the ones an analysis would run with now.
    /// </summary>
    /// <param name="query">May name a crawl, once, as <c>crawl</c>.</param>
    public AnalysisSettings LimitsFor(IQueryCollection query)
    {
        var named = query["crawl"];
        if (named.Count == 0)
        {
            return crawls.AnalysisSettings;
        }

        if (named.Count > 1 || string.IsNullOrEmpty(named[0]))
        {
            throw Problems.BadRequest("crawl must be one crawl's id.");
        }

        var path = crawls.PathOf(named[0]!);
        return EngineErrors.Read(() => IssueReader.ReadAnalysis(path))?.Settings ?? crawls.AnalysisSettings;
    }

    /// <summary>
    /// The last analysis, whether it still stands, every check's count or why it was skipped, and its score, all from one
    /// snapshot of the file, even if another analysis finishes or the crawl is resumed meanwhile. An analysis by an engine
    /// from before the summary is out of date (the engine changed), with no reasons for its skips: its notes say. The
    /// score and the totals it goes by are the analysis's own, as stored: nothing here is counted from the crawl as it is now.
    /// </summary>
    public AnalysisDto Analysis(string id)
    {
        var path = crawls.PathOf(id);
        var results = EngineErrors.Read(() => IssueReader.ReadResults(path)) ?? throw Problems.NotAnalyzed();
        var (analysis, counts, notes) = (results.Analysis, results.Counts, results.Notes);
        var reasons = results.Skipped.ToDictionary(skip => skip.CheckId, StringComparer.Ordinal);
        var outOfDate = results.OutOfDate(crawls.AnalysisSettings);

        // In catalog order. A check from a newer engine, which this one doesn't know, still counts, after the rest.
        List<CheckCountDto> ran =
        [
            .. IssueCatalog.All.Where(check => counts.ContainsKey(check.Id))
                .Select(check => new CheckCountDto(check.Id, counts[check.Id], IssueGroupReader.IsLikelyTemplateCheck(check, counts[check.Id], analysis.PagesAnalyzed)))
        ];
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
                    reasons.TryGetValue(check.Id, out var skip) ? Reason(skip, analysis.Settings ?? crawls.AnalysisSettings) : null))
            ],
            analysis.CrawledUrlCount,
            results.FixFirst,
            results.Score,
            results.NoScoreReason);
    }

    /// <summary>
    /// Why a check was skipped: the analysis's code and sentence, with the code's plain words where it has them, their
    /// limits filled from the settings the analysis ran with.
    /// </summary>
    private static SkipReasonDto Reason(SkippedCheck skip, AnalysisSettings settings)
    {
        var plain = PlainLimits.Fill(PlainWords.SkipReason(skip.Reason), settings);
        return new SkipReasonDto(skip.Reason, skip.Text, plain?.Text, plain?.Action);
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
