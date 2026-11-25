using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>
/// The synthetic site crawled and analysed at revision 0, then switched to revision 1 ("after the fixes"), as its owner
/// would publish them: a test copies the crawl into its sidecar's library (<see cref="FixLoopCrawls.AddTo"/>) and rechecks
/// the copy against the site as it is now.
/// </summary>
public sealed class RecheckSite : IAsyncLifetime
{
    private readonly string _folder = TestSidecar.NewRoot();

    internal SyntheticSiteServer Server { get; private set; } = null!;

    /// <summary>The crawl of revision 0, analysed.</summary>
    public string Then { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = FixLoopCrawls.PAGES });
        Then = Path.Combine(_folder, "then.sicrawl");
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(new CrawlOptions { StartUrl = Server.BaseUri.AbsoluteUri, Concurrency = 8, BrowserFallback = false }, Then)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(EventStreamReader.TIMEOUT)).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(Then);
        Server.Revision = 1;
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        TestSidecar.DeleteRoot(_folder);
    }
}
