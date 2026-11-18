using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A recheck of robots.txt or the sitemaps that the site gives no answer (the connection dropped) changes nothing it
/// couldn't read again: the crawl's robots.txt, its sitemaps and their entries stay, nothing is fixed, and the URL it
/// judges couldn't be checked. A real one, every connection refused, lost a sitemap's 240 entries, stored robots.txt as
/// unreachable, called two checks fixed and the score went from 23 to 0.
/// </summary>
public sealed class RecheckNoAnswerTests : IDisposable
{
    /// <summary>robots.txt as read, but when: read again with an answer, it's the same file read later.</summary>
    private const string ROBOTS = "SELECT origin, status_code, content, error FROM robots";

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    /// <summary>The sitemap index drops the connection: its files, their entries, and the missing file the error is about all stay.</summary>
    [Fact]
    public async Task ASitemapThatGetsNoAnswerKeepsWhatTheCrawlRead()
    {
        var revision = 0;
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => (path, Volatile.Read(ref revision)) switch
        {
            ("/robots.txt", _) => Text($"User-agent: *\nAllow: /\nSitemap: {baseUri}sitemap_index.xml\n"),
            ("/sitemap_index.xml", 1) => new SyntheticResponse(200, "application/xml", null, null, null, Abort: true),
            ("/sitemap_index.xml", _) => Xml($"""<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><sitemap><loc>{baseUri}pages.xml</loc></sitemap><sitemap><loc>{baseUri}missing.xml</loc></sitemap></sitemapindex>"""),
            ("/pages.xml", _) => Xml($"""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>{baseUri}</loc></url><url><loc>{baseUri}a</loc></url></urlset>"""),
            _ => Pages(path)
        });

        var path = await CrawlAsync(site);
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
        var before = _files.NewPath();
        File.Copy(path, before);

        Volatile.Write(ref revision, 1);
        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("sitemap-error"));

        Assert.Equal(CrawlStatus.Completed, run.Status);
        AssertSame(before, path, "sitemaps", "sitemap_urls");
        Assert.Equal(TestCrawls.Rows(before, ROBOTS), TestCrawls.Rows(path, ROBOTS));
        Assert.Equal((0, 0, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        Assert.Equal(new RecheckAnswerCounts(Read: 0, RateLimited: 0, Blocked: 0, RobotsTxt: 0, NoAnswer: 1, NotAsked: 0), recheck.Answers);
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
    }

    /// <summary>robots.txt drops the connection: the crawl's stays, and the check it names isn't fixed.</summary>
    [Fact]
    public async Task ARobotsTxtThatGetsNoAnswerKeepsWhatTheCrawlRead()
    {
        var revision = 0;
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => (path, Volatile.Read(ref revision)) switch
        {
            ("/robots.txt", 1) => new SyntheticResponse(200, "text/plain", null, null, null, Abort: true),
            ("/robots.txt", _) => Text("User-agent: *\nAllow: /\n"),
            ("/sitemap.xml", _) => Xml($"""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>{baseUri}</loc></url></urlset>"""),
            _ => Pages(path)
        });

        var path = await CrawlAsync(site);
        Assert.Single(IssueReader.ReadIssues(path, "robots-txt-no-sitemap", 10));
        var before = _files.NewPath();
        File.Copy(path, before);

        Volatile.Write(ref revision, 1);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("robots-txt-no-sitemap"), new AnalysisSettings());
        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, plan with { Kinds = [RecheckKind.RobotsTxt] });

        Assert.Equal(CrawlStatus.Completed, run.Status);
        AssertSame(before, path, "robots");
        Assert.Equal((0, 0, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        Assert.Equal(1, recheck.Answers!.NoAnswer);
        Assert.Single(IssueReader.ReadIssues(path, "robots-txt-no-sitemap", 10));
    }

    private async Task<string> CrawlAsync(TestWebHost site)
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }

    private static void AssertSame(string before, string after, params string[] tables)
    {
        foreach (var table in tables)
        {
            Assert.True(TestCrawls.Rows(before, $"SELECT * FROM {table}").SequenceEqual(TestCrawls.Rows(after, $"SELECT * FROM {table}")), $"{table} changed");
        }
    }

    private static SyntheticResponse Pages(string path) => path switch
    {
        "/" => Html("Home", """<a href="/a">A</a> <a href="/b">B</a>"""),
        "/a" => Html("A", string.Empty),
        "/b" => Html("B", string.Empty),
        _ => new SyntheticResponse(404, "text/html; charset=utf-8", "<html><body>Not found</body></html>", null, null)
    };

    private static SyntheticResponse Html(string title, string body) =>
        new(200, "text/html; charset=utf-8", $"<html lang=\"en\"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>", null, null);

    private static SyntheticResponse Text(string text) => new(200, "text/plain", text, null, null);

    private static SyntheticResponse Xml(string xml) => new(200, "application/xml", xml, null, null);
}
