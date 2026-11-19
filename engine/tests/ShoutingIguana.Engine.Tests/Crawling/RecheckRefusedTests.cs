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
/// A recheck the site turns away (it asks the crawler to slow down, as a Shopify store does, or a firewall answers in place
/// of the page) learns nothing new of the pages it was refused, so they stay as the crawl read them: their issues stay,
/// the check's count and the score don't move, and each page is not checked, never fixed. A real one, every page answered
/// 429, stored the refusals over the pages the crawl had read, so their links went, the check fell from 7 to 0 and the
/// problem page said "Nothing wrong".
/// </summary>
public sealed class RecheckRefusedTests : IDisposable
{
    private const string CHECK = "broken-internal-links";

    /// <summary>What the crawl read of each URL and where its pages link: a refused recheck leaves both as they were.</summary>
    private const string READ = "SELECT u.url, r.outcome, r.status_code, r.error, r.fetched_at FROM responses r JOIN urls u ON u.id = r.url_id ORDER BY u.url";

    private const string LINKS = "SELECT f.url, t.url FROM links l JOIN urls f ON f.id = l.from_id JOIN urls t ON t.id = l.to_id ORDER BY f.url, t.url";

    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<string> Refusals() => ["rate-limited", "shopify", "firewall"];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ARecheckTheSiteRefusesKeepsTheIssuesAndTheScore(string refusal)
    {
        var refusing = 0;
        await using var site = await TestWebHost.StartAsync((_, _) => (path, _) => Volatile.Read(ref refusing) == 1 && path != "/robots.txt"
            ? Refused(refusal)
            : Pages(path));

        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 2 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var flagged = IssueReader.ReadAll(path, CHECK).Select(issue => issue.Url).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(2, flagged.Count);
        var score = IssueReader.ReadResults(path)!.Score!.Value;
        var read = TestCrawls.Rows(path, READ);
        var links = TestCrawls.Rows(path, LINKS);

        Volatile.Write(ref refusing, 1);
        var plan = RecheckPlanReader.Plan(path, new RecheckAsk(CHECK), new AnalysisSettings());
        Assert.Null(plan.Refused);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        CrawlSummary run;
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Recheck(new RecheckRequest(path, plan, CrawlDatabase.ReadOptions(path))))
        {
            run = await FakeClock.PumpAsync(time, job.Completion, realLimit: TimeSpan.FromMinutes(2));
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var recheck = RecheckReader.Read(path, run.RecheckId!.Value)!;

        Assert.NotEqual(CrawlStatus.Failed, run.Status);
        Assert.Equal(read, TestCrawls.Rows(path, READ));
        Assert.Equal(links, TestCrawls.Rows(path, LINKS));
        Assert.Equal(flagged, IssueReader.ReadAll(path, CHECK).Select(issue => issue.Url).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(score, IssueReader.ReadResults(path)!.Score!.Value);
        var result = recheck.Result!;
        Assert.Equal((0, 0, 0), (result.Fixed, result.Still, result.New));
        Assert.Equal(2, result.NotChecked + result.NotRechecked);
        Assert.True(result.NotChecked > 0, $"{result.NotChecked} not checked, {result.NotRechecked} not rechecked");
        Assert.Equal(result.ScoreBefore, result.ScoreAfter);
        Assert.Equal(0, recheck.Answers!.Read);
    }

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
        "shopify" => new SyntheticResponse(429, "text/plain", "Too Many Requests", null, null,
            new Dictionary<string, string> { ["Retry-After"] = "5", ["powered-by"] = "Shopify" }),
        _ => new SyntheticResponse(403, "text/html; charset=utf-8", "<html><body>Just a moment...</body></html>", null, null,
            new Dictionary<string, string> { ["cf-mitigated"] = "challenge", ["server"] = "cloudflare" })
    };

    private static SyntheticResponse Html(string title, string body) =>
        new(200, "text/html; charset=utf-8", $"<html lang=\"en\"><head><title>{title}</title></head><body><h1>{title}</h1>{body}</body></html>", null, null);
}
