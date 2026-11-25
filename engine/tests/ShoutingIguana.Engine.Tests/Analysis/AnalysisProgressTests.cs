using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>An analysis says how far it has got: every phase in order, each from nothing done to all of it, a few times a second.</summary>
public sealed class AnalysisProgressTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _site = null!;

    public async Task InitializeAsync() => _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });

    public async Task DisposeAsync()
    {
        await _site.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task AnAnalysisReportsEveryPhaseInOrderFromNothingDoneToAllOfIt()
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = _site.BaseUri.AbsoluteUri, BrowserFallback = false }, path)).Status);
        var reports = new RecordingProgress();

        var summary = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path, null, reports);

        var seen = reports.Reports;
        AnalysisPhase[] order = [AnalysisPhase.Reading, AnalysisPhase.PageRules, AnalysisPhase.SiteRules, AnalysisPhase.UrlSummary, AnalysisPhase.Writing];
        Assert.Equal(order, seen.Select(report => report.Phase).Distinct());
        Assert.Equal(seen.Select(report => report.Phase).Order(), seen.Select(report => report.Phase));
        foreach (var phase in order)
        {
            var reported = seen.Where(report => report.Phase == phase).ToList();
            Assert.Equal(0, reported[0].Done);
            Assert.Equal(reported[^1].Total, reported[^1].Done);
            Assert.All(reported, report => Assert.InRange(report.Done, 0, report.Total));
            Assert.Equal(reported.Select(report => report.Done).Order(), reported.Select(report => report.Done));
        }

        // What each phase counted: the crawl's URLs, the pages the analysis read, its cross-page rules, the summary's rows.
        var urls = TestCrawls.Count(path, "SELECT COUNT(*) FROM urls");
        Assert.Equal(urls, Last(seen, AnalysisPhase.Reading).Total);
        Assert.Equal(summary.PagesAnalyzed, Last(seen, AnalysisPhase.PageRules).Total);
        Assert.Equal(summary.PagesAnalyzed, seen.First(report => report.Phase == AnalysisPhase.PageRules).Total);
        Assert.Equal(14, Last(seen, AnalysisPhase.SiteRules).Total);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM url_summary"), Last(seen, AnalysisPhase.UrlSummary).Total);
        Assert.Equal(urls, Last(seen, AnalysisPhase.UrlSummary).Total);
        Assert.Equal(new AnalysisProgress(AnalysisPhase.Writing, 1, 1), seen[^1]);
    }

    [Fact]
    public void ProgressIsReportedAFewTimesASecondWhateverThePace()
    {
        var time = new FakeTimeProvider();
        var reports = new RecordingProgress();
        var reporter = new AnalysisProgressReporter(reports, time);

        reporter.Start(AnalysisPhase.PageRules, 100_000);
        for (var page = 1; page <= 50_000; page++)
        {
            reporter.Advance(page);
        }

        // Only the start: no time has passed.
        Assert.Equal([new AnalysisProgress(AnalysisPhase.PageRules, 0, 100_000)], reports.Reports);

        time.Advance(AnalysisProgressReporter.INTERVAL);
        reporter.Advance(50_001);
        reporter.Advance(50_002);
        time.Advance(AnalysisProgressReporter.INTERVAL / 2);
        reporter.Advance(50_003);
        Assert.Equal(new AnalysisProgress(AnalysisPhase.PageRules, 50_001, 100_000), reports.Reports[^1]);
        Assert.Equal(2, reports.Reports.Count);

        // The end is always reported, with the count it ended on as its total.
        reporter.Finish(99_000);
        Assert.Equal(new AnalysisProgress(AnalysisPhase.PageRules, 99_000, 99_000), reports.Reports[^1]);
        reporter.Start(AnalysisPhase.SiteRules, 9);
        reporter.Advance(4);
        reporter.Finish();
        Assert.Equal(new AnalysisProgress(AnalysisPhase.SiteRules, 4, 4), reports.Reports[^1]);
        Assert.Equal(5, reports.Reports.Count);
    }

    [Fact]
    public void WithoutAListenerNothingIsReported()
    {
        var reporter = new AnalysisProgressReporter(null, new FakeTimeProvider());
        reporter.Start(AnalysisPhase.Reading, 10);
        reporter.Advance(5);
        reporter.Finish();
    }

    private static AnalysisProgress Last(IReadOnlyList<AnalysisProgress> reports, AnalysisPhase phase) => reports.Last(report => report.Phase == phase);

    /// <summary>Keeps every report as it comes, on the analysis's thread.</summary>
    private sealed class RecordingProgress : IProgress<AnalysisProgress>
    {
        private readonly List<AnalysisProgress> _reports = [];

        public IReadOnlyList<AnalysisProgress> Reports
        {
            get
            {
                lock (_reports)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(AnalysisProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }
}
