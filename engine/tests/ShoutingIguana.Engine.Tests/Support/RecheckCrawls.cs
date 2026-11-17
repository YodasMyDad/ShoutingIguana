using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// The synthetic site crawled and analysed at revision 0, then switched to revision 1, as its owner would publish fixes:
/// each recheck test rechecks a <see cref="Copy"/> of the crawl against the site after the fixes.
/// </summary>
public sealed class RecheckCrawls : IAsyncLifetime
{
    /// <summary>Products 184 to 186 hold the duplicate-title cluster revision 1 renames, and two of each broken-link fate are planted.</summary>
    public const int PAGES = 250;

    private readonly CrawlFiles _files = new();

    public SyntheticSiteServer Server { get; private set; } = null!;

    /// <summary>The crawl of revision 0, analysed.</summary>
    public string Then { get; private set; } = string.Empty;

    public CrawlOptions Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });
        Options = new CrawlOptions { StartUrl = Server.BaseUri.AbsoluteUri, Concurrency = 8 };
        Then = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options, Then)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(Then);
        Server.Revision = 1;
    }

    /// <summary>A copy of a crawl file, deleted with the fixture.</summary>
    public string Copy(string path)
    {
        var copy = _files.NewPath();
        File.Copy(path, copy);
        return copy;
    }

    public string NewPath() => _files.NewPath();

    /// <summary>Plans a recheck of the ask on <paramref name="path"/>, runs it, analyses the crawl, and returns the run's summary and the recheck as settled.</summary>
    public static async Task<(CrawlSummary Run, RecheckRecord Recheck)> RecheckAsync(string path, RecheckAsk ask, Func<CrawlOptions, CrawlOptions>? options = null,
        int limit = RecheckPlanReader.LIMIT)
    {
        var plan = RecheckPlanReader.Plan(path, ask, new AnalysisSettings(), limit);
        Assert.Null(plan.Refused);
        var stored = CrawlDatabase.ReadOptions(path);
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, options?.Invoke(stored) ?? stored));
        var run = await job.Completion.WaitAsync(TestCrawls.TIMEOUT);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return (run, RecheckReader.Read(path, run.RecheckId!.Value)!);
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        _files.Dispose();
    }
}

[CollectionDefinition(NAME)]
public sealed class RecheckCrawlsCollection : ICollectionFixture<RecheckCrawls>
{
    public const string NAME = "Rechecks of the synthetic site";
}
