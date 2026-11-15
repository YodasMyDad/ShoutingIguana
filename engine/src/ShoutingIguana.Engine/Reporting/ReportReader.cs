using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// Reads what the report and the workbook's summary say of a crawl, through the engine's own readers: the last analysis
/// with its counts and skipped checks, the crawl's headline numbers and options, and each problem's first pages. It only
/// reads. What it reads is small whatever the crawl's size: counts, and a few rows a check.
/// </summary>
internal static class ReportReader
{
    /// <param name="examples">The first pages to read for a problem of each severity; none reads none.</param>
    /// <param name="outOfDateBy">The thresholds an analysis would run with now, to say whether the results still stand; null doesn't judge.</param>
    /// <exception cref="ExportException">
    /// <see cref="ExportFailure.NotAnalyzed"/> when the crawl has never been analysed: there is no report of it.
    /// <see cref="ExportFailure.CrawlChanged"/> when it was analysed again while this read it.
    /// </exception>
    /// <param name="listing">Which checks the lists hold: the hidden ones and those outside a view left out. Null lists every check.</param>
    public static ReportModel Read(string crawlPath, Func<IssueSeverity, int> examples, ExportSettings export, AnalysisSettings? outOfDateBy = null,
        CancellationToken cancellationToken = default, ReportOptions? listing = null)
    {
        bool Lists(string checkId) => listing?.Lists(checkId) ?? true;
        var results = IssueReader.ReadResults(crawlPath) ?? throw new ExportException(ExportFailure.NotAnalyzed, NOT_ANALYZED);
        var stats = CrawlStatsReader.Read(crawlPath, export.Cache, cancellationToken);
        var summary = CrawlSummaryReader.Read(crawlPath, cancellationToken);
        var options = CrawlDatabase.ReadOptions(crawlPath);
        var reasons = results.Skipped.ToDictionary(skip => skip.CheckId, StringComparer.Ordinal);
        var counts = results.Counts;

        // The analysis's own numbers: the pages it read and the URLs crawled when it ran, which its counts are shares of.
        var settings = ReportSources.Settings(results);
        var score = results.Score;
        var pages = results.Analysis.PagesAnalyzed;
        var urls = score?.Covers.Urls ?? results.Analysis.CrawledUrlCount ?? summary.CrawledUrlCount ?? stats.Outcomes.Values.Sum();

        // The site's own addresses its robots.txt refused, as the analysis counted them: the score's, or what the check
        // of them found where there is no score. The crawl's count of the outcome has other websites' among it.
        var blocked = score?.Covers.BlockedByRobots ?? counts.GetValueOrDefault(IssueCatalog.BLOCKED_BY_ROBOTS.Id);

        // That the crawl went on since the analysis needs no thresholds to tell; the rest is judged only when asked.
        var judged = results.OutOfDate(outOfDateBy ?? results.Analysis.Settings ?? new AnalysisSettings());
        IReadOnlyList<OutOfDateReason> outOfDate = outOfDateBy is not null ? judged : judged.Contains(OutOfDateReason.CrawlChanged) ? [OutOfDateReason.CrawlChanged] : [];
        var running = stats.Status is CrawlStatus.Running or CrawlStatus.Paused;
        var ran = IssueCatalog.All.Count(check => counts.ContainsKey(check.Id));
        var fixFirst = ReportSources.FixFirst(results);

        List<ReportProblem> problems = [];
        foreach (var id in fixFirst.Where(Lists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var check = IssueCatalog.Find(id)!;
            var listed = examples(check.Severity);
            var rows = listed > 0 ? IssueReader.ReadIssues(crawlPath, id, listed) : [];
            var (foundOn, targets) = References(crawlPath, check, rows, export, cancellationToken);
            var group = check.Grouping == IssueGrouping.None ? null : IssueGroupReader.ReadGroups(crawlPath, id, 0, 1, export.Cache, cancellationToken)?.Groups.FirstOrDefault();
            problems.Add(new ReportProblem(
                problems.Count + 1,
                check,
                ReportSources.Words(check, settings),
                counts[id],
                score?.Lines.FirstOrDefault(line => line.CheckId == id),
                [.. rows.Select(row => new ReportExample(row.Url, row.Detail, row.RelatedUrl, foundOn.GetValueOrDefault(row.UrlId), targets.TryGetValue(row.UrlId, out var named) ? named : null))],
                group is { LikelyTemplate: true } ? new ReportOneFix(group.Pages, group.RelatedUrl, group.Detail) : null));
        }

        // Whatever was read above is one analysis's only if that analysis is still the last.
        if (IssueReader.ReadAnalysis(crawlPath)?.AnalyzedAt != results.Analysis.AnalyzedAt)
        {
            throw new ExportException(ExportFailure.CrawlChanged, GridExport.CRAWL_CHANGED);
        }

        var now = export.Clock.GetUtcNow();
        return new ReportModel
        {
            Site = SiteOf(stats.StartUrl),
            StartUrl = stats.StartUrl,
            Origin = OriginOf(Home(crawlPath, stats.StartUrl, cancellationToken)),
            Status = stats.Status,
            StillRunning = running,
            StartedAt = stats.StartedAt,
            FinishedAt = stats.FinishedAt,
            TimeCrawling = summary.TimeCrawling,
            AnalyzedAt = results.Analysis.AnalyzedAt,
            AnalysisEngineVersion = results.Analysis.EngineVersion,
            OutOfDate = outOfDate,
            Pages = pages,
            Urls = urls,
            IndexablePages = stats.IndexablePageCount,
            Queued = stats.QueuedCount,
            NotRead = stats.SkipReasons,
            BlockedByRobots = blocked,
            Options = options,
            ChecksInCatalog = IssueCatalog.All.Count,
            ChecksRun = ran,
            Problems = problems,
            Skipped =
            [
                .. IssueCatalog.All.Where(check => !counts.ContainsKey(check.Id) && Lists(check.Id)).Select(check =>
                {
                    var reason = reasons.GetValueOrDefault(check.Id);
                    var (why, action) = ReportSources.Skipped(reason, settings);
                    return new ReportSkipped(check, ReportSources.Words(check, settings), reason?.Reason, why, action);
                })
            ],
            Passed =
            [
                .. IssueCatalog.All.Where(check => counts.GetValueOrDefault(check.Id, -1) == 0 && Lists(check.Id))
                    .Select(check => new ReportPassed(check, ReportSources.Words(check, settings)))
            ],
            HiddenProblems = listing is null ? 0 : fixFirst.Count(id => listing.HiddenChecks.Contains(id, StringComparer.Ordinal)),
            Narrowed = listing?.Checks is not null,
            Score = score,
            NoScore = results.NoScoreReason,
            MadeAt = now,
            TimeZone = export.Clock.LocalTimeZone,
            EngineVersion = EngineInfo.Version
        };
    }

    /// <summary>
    /// Where to fix each of a problem's first pages, in one read: for a check whose URLs are referred to, where each is found
    /// (the first pages linking to it, and the sitemaps listing it); for one whose pages hold the bad references, how many
    /// URLs each names. Nothing for other checks, or none listed.
    /// </summary>
    private static (IReadOnlyDictionary<int, Storage.Models.UrlFoundOn> FoundOn, IReadOnlyDictionary<int, long> Targets) References(string crawlPath,
        IssueDefinition check, IReadOnlyList<Storage.Models.IssueRow> rows, ExportSettings export, CancellationToken cancellationToken)
    {
        List<int> ids = [.. rows.Select(row => row.UrlId).Distinct().Take(FoundOnReader.MAX_URLS)];
        if (ids.Count == 0)
        {
            return (new Dictionary<int, Storage.Models.UrlFoundOn>(), new Dictionary<int, long>());
        }

        return check.References switch
        {
            IssueReferences.Sources => (FoundOnReader.ReadFoundOn(crawlPath, ids, LISTED_SOURCES, export.Cache, cancellationToken).ToDictionary(url => url.UrlId),
                new Dictionary<int, long>()),
            IssueReferences.Targets => (new Dictionary<int, Storage.Models.UrlFoundOn>(),
                (FoundOnReader.ReadTargets(crawlPath, check.Id, ids, 0, export.Cache, cancellationToken) ?? []).ToDictionary(url => url.UrlId, url => url.Total)),
            _ => (new Dictionary<int, Storage.Models.UrlFoundOn>(), new Dictionary<int, long>())
        };
    }

    /// <summary>The pages linking to each listed URL that the report names, before "and N more".</summary>
    internal const int LISTED_SOURCES = 3;

    /// <summary>The site's home page: where the start URL's redirects end, or the start URL when the crawl has no such page.</summary>
    private static string Home(string crawlPath, string startUrl, CancellationToken cancellationToken)
    {
        try
        {
            return UrlGridReader.Lookup(crawlPath, "/", cancellationToken)?.Url ?? startUrl;
        }
        catch (UrlQueryException)
        {
            // A start URL that isn't one (a file changed by hand): there is no origin to leave off, and every address is shown whole.
            return startUrl;
        }
    }

    /// <summary>What a report of a crawl never analysed says: there is none.</summary>
    public const string NOT_ANALYZED = "The results haven’t been worked out for this crawl, so there is no report of it yet. Analyse it first.";

    /// <summary>The site as its owner names it: the host, without a leading www.</summary>
    internal static string SiteOf(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.IdnHost : url;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>A URL's origin: its scheme and host, and its port unless it's the scheme's own.</summary>
    internal static string OriginOf(string url) =>
        UrlNormalizer.TryNormalize(url, out var normalized) && Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) ? parsed.GetLeftPart(UriPartial.Authority) : url;
}
