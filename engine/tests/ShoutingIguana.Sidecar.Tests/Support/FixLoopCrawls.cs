using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>
/// The synthetic site crawled at revision 0 and again at revision 1 ("after the fixes"), each analysed, in a folder of
/// their own: a test copies them into its sidecar's library (<see cref="AddTo"/>) and changes the copies.
/// </summary>
public sealed class FixLoopCrawls : IAsyncLifetime
{
    /// <summary>Enough pages for revision 1's plants: products 184 to 186 hold the duplicate-title cluster it renames.</summary>
    public const int PAGES = 250;

    private readonly string _folder = TestSidecar.NewRoot();

    internal SyntheticSiteServer Server { get; private set; } = null!;

    /// <summary>The crawl of revision 0, analysed.</summary>
    public string Then { get; private set; } = string.Empty;

    /// <summary>The crawl of revision 1, with the same options, analysed.</summary>
    public string Now { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });
        Then = await CrawlAsync("then");
        Server.Revision = 1;
        Now = await CrawlAsync("now");
        Server.Revision = 0;
    }

    /// <summary>
    /// A copy of a crawl in a sidecar's data folder, named <paramref name="name"/>, started <paramref name="minutes"/> after
    /// the original (the order of a site's crawls), and changed as a test needs.
    /// </summary>
    internal static string AddTo(TestSidecar sidecar, string source, string name, int minutes = 0, Action<string>? change = null)
    {
        Directory.CreateDirectory(sidecar.DataDirectory);
        var path = Path.Combine(sidecar.DataDirectory, name + ".sicrawl");
        File.Copy(source, path);
        if (minutes != 0)
        {
            TestCrawlFiles.ShiftStartedAt(path, TimeSpan.FromMinutes(minutes));
        }

        change?.Invoke(path);
        return path;
    }

    /// <summary>A URL of the synthetic site, by its path.</summary>
    public string Url(string path) => new Uri(Server.BaseUri, path).AbsoluteUri;

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        TestSidecar.DeleteRoot(_folder);
    }

    private async Task<string> CrawlAsync(string name)
    {
        var path = Path.Combine(_folder, name + ".sicrawl");
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(new CrawlOptions { StartUrl = Server.BaseUri.AbsoluteUri, Concurrency = 8, BrowserFallback = false }, path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(EventStreamReader.TIMEOUT)).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }
}
