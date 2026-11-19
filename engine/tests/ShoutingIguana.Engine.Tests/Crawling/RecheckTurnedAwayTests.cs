using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A recheck never loses what the crawl read of the site's sitemaps, its robots.txt or its pages because the site turned
/// the recheck away, or because it was stopped part-way. A sitemap the site answers 429 or a firewall's challenge for, a
/// robots.txt it can't serve (429 or a server error), and a page refused only because robots.txt couldn't be read all say
/// nothing new: the sitemaps and their entries, robots.txt, the pages and their links stay as the crawl read them, and
/// what the recheck judges is not checked, never fixed. A real recheck of 18 November lost a sitemap's 240 entries and
/// left its row saying 240: these are the other ways the list could have gone.
/// </summary>
public sealed class RecheckTurnedAwayTests : IDisposable
{
    private const string ROBOTS = "SELECT origin, status_code, content, error FROM robots";
    private const string SITEMAPS = "SELECT * FROM sitemaps ORDER BY id";
    private const string ENTRIES = "SELECT s.url, u.url FROM sitemap_urls m JOIN sitemaps s ON s.id = m.sitemap_id JOIN urls u ON u.id = m.url_id ORDER BY s.url, u.url";
    private const string READ = "SELECT u.url, r.outcome, r.status_code, r.error, r.fetched_at FROM responses r JOIN urls u ON u.id = r.url_id ORDER BY u.url";
    private const string LINKS = "SELECT f.url, t.url FROM links l JOIN urls f ON f.id = l.from_id JOIN urls t ON t.id = l.to_id ORDER BY f.url, t.url";

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    /// <summary>The shape of the real crawl: robots.txt names no sitemap, /sitemap.xml lists the pages, and nothing changed since.</summary>
    [Fact]
    public async Task ARecheckOfRobotsTxtAndTheSitemapsKeepsTheListWhenNothingChanged()
    {
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/robots.txt" => Text("User-agent: *\nAllow: /\n"),
            "/sitemap.xml" => Xml(Urlset(baseUri, "", "a", "b")),
            _ => Pages(path)
        });

        var path = await CrawlAsync(site);
        Assert.Single(IssueReader.ReadIssues(path, "robots-txt-no-sitemap", 10));
        var (sitemaps, entries) = (TestCrawls.Rows(path, SITEMAPS), TestCrawls.Rows(path, ENTRIES));
        Assert.Equal(3, entries.Count);

        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("robots-txt-no-sitemap"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        Assert.Equal(sitemaps, TestCrawls.Rows(path, SITEMAPS));
        Assert.Equal(entries, TestCrawls.Rows(path, ENTRIES));
        Assert.Equal((0, 1), (recheck.Result!.Fixed, recheck.Result.Still));
    }

    public static TheoryData<string> Refusals() => ["rate-limited", "firewall"];

    /// <summary>A sitemap the site turns the recheck away from (429 at every try, or a firewall's challenge) stays as the crawl read it, with its entries.</summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ASitemapTheSiteTurnsAwayKeepsWhatTheCrawlRead(string refusal)
    {
        var refusing = 0;
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/pages.xml" when Volatile.Read(ref refusing) == 1 => Refused(refusal),
            _ => IndexedSite(baseUri, path)
        });

        var path = await CrawlAsync(site);
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
        var (sitemaps, entries, robots) = (TestCrawls.Rows(path, SITEMAPS), TestCrawls.Rows(path, ENTRIES), TestCrawls.Rows(path, ROBOTS));
        Assert.Equal(2, entries.Count);

        Volatile.Write(ref refusing, 1);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("sitemap-error"), new AnalysisSettings());
        var (run, recheck) = await RecheckOnAFakeClockAsync(path, plan);

        Assert.Equal(CrawlStatus.Completed, run.Status);
        Assert.Equal(sitemaps, TestCrawls.Rows(path, SITEMAPS));
        Assert.Equal(entries, TestCrawls.Rows(path, ENTRIES));
        Assert.Equal(robots, TestCrawls.Rows(path, ROBOTS));
        Assert.Equal((0, 0, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
    }

    /// <summary>
    /// The connection breaks while the recheck reads a sitemap's body: the part that came is not what the site lists now.
    /// The sitemap stays as the crawl read it, every entry, and the check isn't settled either way.
    /// </summary>
    [Fact]
    public async Task ASitemapWhoseBodyBreaksOffKeepsWhatTheCrawlRead()
    {
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/pages.xml" => Xml(Urlset(baseUri, "", "a", "b")),
            _ => IndexedSite(baseUri, path)
        });

        var path = await CrawlAsync(site);
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
        var (sitemaps, entries) = (TestCrawls.Rows(path, SITEMAPS), TestCrawls.Rows(path, ENTRIES));
        Assert.Equal(3, entries.Count);

        site.CutShort("/pages.xml");
        var (run, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("sitemap-error"));

        Assert.Equal((CrawlStatus.Completed, RecheckStatus.Completed), (run.Status, recheck.Status));
        Assert.Equal(sitemaps, TestCrawls.Rows(path, SITEMAPS));
        Assert.Equal(entries, TestCrawls.Rows(path, ENTRIES));
        Assert.Equal((0, 0, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
    }

    /// <summary>A crawl has nothing older to keep: the part of a sitemap that came before the connection broke is listed, and the sitemap says it failed.</summary>
    [Fact]
    public async Task ACrawlStoresThePartOfASitemapThatCameBeforeItBrokeOff()
    {
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/pages.xml" => Xml(Urlset(baseUri, "", "a", "b")),
            _ => IndexedSite(baseUri, path)
        });
        site.CutShort("/pages.xml");

        var path = await CrawlAsync(site);

        var pages = SitemapListReader.Read(path).Sitemaps.Single(sitemap => sitemap.Url.EndsWith("/pages.xml", StringComparison.Ordinal));
        Assert.Equal((SitemapState.Failed, 200), (pages.State, pages.StatusCode));
        Assert.InRange(TestCrawls.Rows(path, ENTRIES).Count, 0, 2);
    }

    public static TheoryData<int> UnreadableStatuses() => [503, 429];

    /// <summary>
    /// robots.txt can't be served when the recheck asks (a server error, or 429): the crawl's robots.txt stays, and the
    /// sitemaps it declares, which the recheck can't know now, are left as the crawl read them: none goes, none is added.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadableStatuses))]
    public async Task ARobotsTxtTheSiteCantServeKeepsItAndTheSitemaps(int status)
    {
        var failing = 0;
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/robots.txt" when Volatile.Read(ref failing) == 1 => new SyntheticResponse(status, "text/plain", "Try later", null, null),
            _ => IndexedSite(baseUri, path)
        });

        var path = await CrawlAsync(site);
        var (sitemaps, entries, robots) = (TestCrawls.Rows(path, SITEMAPS), TestCrawls.Rows(path, ENTRIES), TestCrawls.Rows(path, ROBOTS));
        Assert.Equal(2, entries.Count);

        Volatile.Write(ref failing, 1);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("sitemap-error"), new AnalysisSettings());
        var (run, recheck) = await RecheckOnAFakeClockAsync(path, plan with { Kinds = [RecheckKind.RobotsTxt, RecheckKind.Sitemaps] });

        Assert.Equal(CrawlStatus.Completed, run.Status);
        Assert.Equal(robots, TestCrawls.Rows(path, ROBOTS));
        Assert.Equal(sitemaps, TestCrawls.Rows(path, SITEMAPS));
        Assert.Equal(entries, TestCrawls.Rows(path, ENTRIES));
        Assert.Equal((0, 0, 1), (recheck.Result!.Fixed, recheck.Result.Still, recheck.Result.NotChecked));
        Assert.Single(IssueReader.ReadIssues(path, "sitemap-error", 10));
        Assert.Empty(IssueReader.ReadIssues(path, "robots-txt-unreachable", 10));
    }

    /// <summary>
    /// A recheck of pages while robots.txt can't be served: the crawler may not fetch them, which says nothing new of
    /// them. They stay as the crawl read them, with their links, and none is fixed or newly "blocked by robots.txt".
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadableStatuses))]
    public async Task PagesRefusedOnlyBecauseRobotsTxtCantBeReadStayAsTheCrawlReadThem(int status)
    {
        var failing = 0;
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => path switch
        {
            "/robots.txt" when Volatile.Read(ref failing) == 1 => new SyntheticResponse(status, "text/plain", "Try later", null, null),
            _ => IndexedSite(baseUri, path)
        });

        var path = await CrawlAsync(site);
        var flagged = IssueReader.ReadAll(path, "broken-internal-links").Select(issue => issue.Url).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(2, flagged.Count);
        var (read, links, robots) = (TestCrawls.Rows(path, READ), TestCrawls.Rows(path, LINKS), TestCrawls.Rows(path, ROBOTS));
        var score = IssueReader.ReadResults(path)!.Score!.Value;

        Volatile.Write(ref failing, 1);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("broken-internal-links"), new AnalysisSettings());
        var (run, recheck) = await RecheckOnAFakeClockAsync(path, plan);

        Assert.NotEqual(CrawlStatus.Failed, run.Status);
        Assert.Equal(read, TestCrawls.Rows(path, READ));
        Assert.Equal(links, TestCrawls.Rows(path, LINKS));
        Assert.Equal(robots, TestCrawls.Rows(path, ROBOTS));
        Assert.Equal(flagged, IssueReader.ReadAll(path, "broken-internal-links").Select(issue => issue.Url).Order(StringComparer.Ordinal).ToList());
        Assert.Empty(IssueReader.ReadIssues(path, "blocked-by-robots", 10));
        Assert.Equal(score, IssueReader.ReadResults(path)!.Score!.Value);
        Assert.Equal((0, 0), (recheck.Result!.Fixed, recheck.Result.New));
        Assert.Equal(0, recheck.Answers!.Read);
    }

    /// <summary>Stopped while a sitemap is being read again: that sitemap's entries are as the crawl read them.</summary>
    [Fact]
    public async Task ARecheckStoppedWhileASitemapIsReadKeepsItsEntries()
    {
        await using var site = await TestWebHost.StartAsync((baseUri, _) => (path, _) => IndexedSite(baseUri, path));

        var path = await CrawlAsync(site);
        var (sitemaps, entries) = (TestCrawls.Rows(path, SITEMAPS), TestCrawls.Rows(path, ENTRIES));
        Assert.Equal(2, entries.Count);

        site.Hold("/pages.xml");
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk("sitemap-error"), new AnalysisSettings());
        CrawlSummary run;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            // Long enough for the index to be read again and pages.xml to be asked for, its old entries put aside.
            await Task.Delay(TimeSpan.FromSeconds(2));
            run = await job.StopAsync().WaitAsync(TestCrawls.TIMEOUT);
        }

        site.Release("/pages.xml");
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.Equal(CrawlStatus.Stopped, run.Status);
        Assert.Equal(sitemaps, TestCrawls.Rows(path, SITEMAPS));
        Assert.Equal(entries, TestCrawls.Rows(path, ENTRIES));
    }

    private async Task<string> CrawlAsync(TestWebHost site)
    {
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }

    /// <summary>Runs the recheck on a clock the test moves, so the waits a refusal asks for pass at once; then analyses, and returns it settled.</summary>
    private static async Task<(CrawlSummary Run, RecheckRecord Recheck)> RecheckOnAFakeClockAsync(string path, RecheckPlan plan)
    {
        Assert.Null(plan.Refused);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        CrawlSummary run;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            run = await FakeClock.PumpAsync(time, job.Completion, realLimit: TimeSpan.FromMinutes(2));
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return (run, RecheckReader.Read(path, run.RecheckId!.Value)!);
    }

    /// <summary>robots.txt declares an index of two sitemaps, one missing; the home page and /a each link to a page that's gone.</summary>
    private static SyntheticResponse IndexedSite(Uri baseUri, string path) => path switch
    {
        "/robots.txt" => Text($"User-agent: *\nAllow: /\nSitemap: {baseUri}sitemap_index.xml\n"),
        "/sitemap_index.xml" => Xml($"""<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><sitemap><loc>{baseUri}pages.xml</loc></sitemap><sitemap><loc>{baseUri}missing.xml</loc></sitemap></sitemapindex>"""),
        "/pages.xml" => Xml(Urlset(baseUri, "", "a")),
        _ => Pages(path)
    };

    private static string Urlset(Uri baseUri, params string[] paths) =>
        $"""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{string.Concat(paths.Select(path => $"<url><loc>{baseUri}{path}</loc></url>"))}</urlset>""";

    private static SyntheticResponse Pages(string path) => path switch
    {
        "/" => Html("Home", """<a href="/a">A</a> <a href="/b">B</a> <a href="/gone">Gone</a>"""),
        "/a" => Html("A", """<a href="/gone">Gone</a>"""),
        "/b" => Html("B", """<a href="/">Home</a>"""),
        _ => new SyntheticResponse(404, "text/html; charset=utf-8", "<html><body>Not found</body></html>", null, null)
    };

    private static SyntheticResponse Refused(string refusal) => refusal switch
    {
        "rate-limited" => new SyntheticResponse(429, "text/plain", "Too Many Requests", null, null, new Dictionary<string, string> { ["Retry-After"] = "5" }),
        _ => new SyntheticResponse(403, "text/html; charset=utf-8", "<html><body>Just a moment...</body></html>", null, null,
            new Dictionary<string, string> { ["cf-mitigated"] = "challenge", ["server"] = "cloudflare" })
    };

    private static SyntheticResponse Html(string title, string body) =>
        new(200, "text/html; charset=utf-8", $"<html lang=\"en\"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>", null, null);

    private static SyntheticResponse Text(string text) => new(200, "text/plain", text, null, null);

    private static SyntheticResponse Xml(string xml) => new(200, "application/xml", xml, null, null);
}
