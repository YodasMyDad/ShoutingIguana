using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;
using static ShoutingIguana.Engine.Storage.Models.IndexabilityTestKind;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// "Indexable, and why" for what the synthetic site doesn't plant: a noindex sent in the X-Robots-Tag header, to the
/// crawler or to another search engine; robots.txt that can't be read, which refuses everything; and a crawl that ignored
/// robots.txt, which never checked it. A crawl that didn't store HTML has none to show.
/// </summary>
public sealed class IndexabilityChecklistTests : IDisposable
{
    private const string HTML = "text/html; charset=utf-8";

    private readonly CrawlFiles _files = new();

    [Fact]
    public async Task ANoindexHeaderFailsTheTestAsAMetaTagDoes()
    {
        await using var site = await TestWebHost.StartAsync((_, _) => Serve(new(StringComparer.Ordinal)
        {
            ["/robots.txt"] = new(200, "text/plain; charset=utf-8", "User-agent: *\nDisallow:\n", null, null),
            ["/"] = Page("Home", string.Empty, """<a href="/header">Header</a> <a href="/both">Both</a> <a href="/other-bot">Other bot</a>"""),
            ["/header"] = Page("Header", string.Empty, "<p>Kept out by a header.</p>", "noindex"),
            ["/both"] = Page("Both", """<meta name="robots" content="noindex">""", "<p>Kept out twice.</p>", "noindex"),
            ["/other-bot"] = Page("Other bot", string.Empty, "<p>Kept out of another search engine only.</p>", "otherbot: noindex")
        }));
        var path = await CrawlAndAnalyseAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri });

        var header = Inspect(path, site, "header");
        Assert.Equal(IndexabilityReason.Noindex, header.Indexability.Reason);
        var sent = Test(header, Noindex);
        Assert.Equal(TestOutcome.Fail, sent.Outcome);
        Assert.True(sent.Header);
        Assert.False(sent.Meta);

        var both = Test(Inspect(path, site, "both"), Noindex);
        Assert.Equal(TestOutcome.Fail, both.Outcome);
        Assert.True(both.Header);
        Assert.True(both.Meta);

        // A noindex for another search engine isn't one for this crawler, nor for Google.
        var other = Inspect(path, site, "other-bot");
        Assert.True(other.Indexability.Indexable);
        Assert.Equal(TestOutcome.Pass, Test(other, Noindex).Outcome);
        Assert.False(Test(other, Noindex).Header);
        Assert.Contains(other.Headers, entry => entry.Name == "x-robots-tag" && entry.Value == "otherbot: noindex");

        // This crawl didn't store HTML.
        var source = UrlInspectorReader.ReadSource(path, header.Id)!;
        Assert.False(source.Stored);
        Assert.Equal(HtmlSourceMissing.NotStored, source.Missing);
        Assert.False(header.SourceStored);
    }

    [Fact]
    public async Task RobotsTxtThatCantBeReadRefusesEverything()
    {
        await using var site = await TestWebHost.StartAsync((_, _) => Serve(new(StringComparer.Ordinal)
        {
            ["/robots.txt"] = new(503, HTML, "<p>Down for maintenance</p>", null, null),
            ["/"] = Page("Home", string.Empty, "<p>The home page.</p>")
        }));

        // robots.txt is asked for again after a cool-down, which a fake clock passes in moments.
        var path = _files.NewPath();
        var time = new FakeTimeProvider();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri }, path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await FakeClock.PumpAsync(time, job.Completion)).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var home = Inspect(path, site, string.Empty);
        Assert.Equal(IndexabilityReason.BlockedByRobots, home.Indexability.Reason);
        var robots = Test(home, RobotsTxt);
        Assert.Equal(TestOutcome.Fail, robots.Outcome);
        Assert.True(robots.Unreachable);
        Assert.Equal(TestOutcome.NotChecked, Test(home, Answered).Outcome);
    }

    [Fact]
    public async Task ACrawlThatIgnoredRobotsTxtNeverCheckedIt()
    {
        await using var site = await TestWebHost.StartAsync((_, _) => Serve(new(StringComparer.Ordinal)
        {
            ["/robots.txt"] = new(200, "text/plain; charset=utf-8", "User-agent: *\nDisallow: /\n", null, null),
            ["/"] = Page("Home", string.Empty, "<p>The home page.</p>")
        }));
        var path = await CrawlAndAnalyseAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, RespectRobotsTxt = false });

        var home = Inspect(path, site, string.Empty);
        Assert.True(home.Indexability.Indexable);
        Assert.Equal([TestOutcome.Pass, TestOutcome.NotChecked, TestOutcome.Pass, TestOutcome.Pass], home.Indexability.Tests.Select(test => test.Outcome));
    }

    public void Dispose() => _files.Dispose();

    private async Task<string> CrawlAndAnalyseAsync(CrawlOptions options)
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options with { Concurrency = 2 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }

    private static UrlDetail Inspect(string path, TestWebHost site, string relative) =>
        UrlInspectorReader.Read(path, UrlGridReader.Lookup(path, site.BaseUri.AbsoluteUri + relative)!.Id)!;

    private static IndexabilityTest Test(UrlDetail url, IndexabilityTestKind kind) => url.Indexability.Tests.Single(test => test.Test == kind);

    private static Func<string, bool, SyntheticResponse> Serve(Dictionary<string, SyntheticResponse> responses) =>
        (path, _) => responses.TryGetValue(path, out var response) ? response : new SyntheticResponse(404, HTML, "<p>Not found</p>", null, null);

    private static SyntheticResponse Page(string title, string head, string body, string? robotsHeader = null) => new(200, HTML,
        $"""<!DOCTYPE html><html lang="en"><head><title>{title}</title>{head}</head><body><h1>{title}</h1>{body}<p><a href="/">Home</a></p></body></html>""",
        null, null, robotsHeader is null ? null : new Dictionary<string, string> { ["X-Robots-Tag"] = robotsHeader });
}
