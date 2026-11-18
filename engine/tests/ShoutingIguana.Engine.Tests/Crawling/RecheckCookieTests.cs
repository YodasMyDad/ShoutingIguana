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
/// A session cookie lands on whichever page a fresh visitor asks first: the crawl's cookie jar had it after the home page,
/// so only the home page was flagged; a recheck starts with an empty jar, so it lands on another page. A recheck never
/// calls a cookie problem new when the crawl flagged the same cookie on another page, nor fixed because a page didn't set
/// its cookie again: only when the page sets it again, protected. A cookie the crawl never saw is new.
/// </summary>
public sealed class RecheckCookieTests : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task ASessionCookieThatLandsOnAnotherPageIsNeitherNewNorFixed()
    {
        var revision = 0;
        await using var site = await TestWebHost.StartAsync((_, _) => (path, _) => (path, Volatile.Read(ref revision)) switch
        {
            ("/", 0) => Page("Home", "sessionid=crawl; Path=/", "/a", "/b"),
            ("/", 2) => Page("Home", "sessionid=fixed; Path=/; HttpOnly", "/a", "/b"),
            ("/", _) => Page("Home", null, "/a", "/b"),
            ("/a", 1) => Page("A", "sessionid=recheck; Path=/"),
            ("/a", _) => Page("A", null),
            ("/b", 1) => Page("B", "authtoken=new; Path=/"),
            ("/b", _) => Page("B", null),
            _ => new SyntheticResponse(404, "text/html; charset=utf-8", "<html><body>Not found</body></html>", null, null)
        });

        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, Concurrency = 1 }, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path)["cookies-insecure"]);
        var ids = new[] { "", "a", "b" }.Select(page => (int)TestCrawls.Count(path, $"SELECT id FROM urls WHERE url = '{site.BaseUri}{page}'")).ToList();
        var fixedCopy = _files.NewPath();
        File.Copy(path, fixedCopy);

        // The recheck's fresh session lands on /a; the home page doesn't set it again; /b sets a cookie the crawl never saw.
        Volatile.Write(ref revision, 1);
        var (_, recheck) = await RecheckCrawls.RecheckAsync(path, new RecheckAsk("cookies-insecure", UrlIds: ids));
        var outcomes = Outcomes(path, recheck.Id, site.BaseUri);
        Assert.Equal((RecheckOutcome.NotChecked, RecheckOutcome.NotChecked, RecheckOutcome.New), (outcomes["/"], outcomes["/a"], outcomes["/b"]));
        Assert.Equal((0, 1), (recheck.Result!.Fixed, recheck.Result.New));

        // The home page sets its session cookie again, protected now: that is fixed.
        Volatile.Write(ref revision, 2);
        (_, recheck) = await RecheckCrawls.RecheckAsync(fixedCopy, new RecheckAsk("cookies-insecure"));
        Assert.Equal((1, 1, 0), (recheck.Judged, recheck.Result!.Fixed, recheck.Result.New));
    }

    private static Dictionary<string, RecheckOutcome?> Outcomes(string path, int recheck, Uri site) =>
        RecheckReader.ReadRows(path, recheck, null, [RecheckRole.Judged], 0, 100)!.Rows
            .ToDictionary(row => row.Url[(site.AbsoluteUri.Length - 1)..], row => row.Outcome);

    private static SyntheticResponse Page(string title, string? cookie, params string[] links) =>
        new(200, "text/html; charset=utf-8",
            $"<html><head><title>{title}</title></head><body><h1>{title}</h1>{string.Concat(links.Select(link => $"<a href=\"{link}\">{link}</a>"))}</body></html>",
            null, null, cookie is null ? null : new Dictionary<string, string> { ["Set-Cookie"] = cookie });
}
