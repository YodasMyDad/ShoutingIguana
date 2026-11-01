using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;
using Xunit.Abstractions;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// The site's other versions (bare domain or www, http or https), probed at the start of a crawl and judged by the
/// analysis. The loopback server answers for every host as the crawl's proxy, so no DNS is needed. It answers http
/// only: https through it fails, as a site without TLS does, so the https:// versions are always dead here. Most tests
/// compare the issues with those of the same site whose www version redirects home as it should, so anything a
/// version changes besides what the test expects shows up.
/// </summary>
public sealed class SiteVersionCrawlTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string BARE = "http://example.com";
    private const string WWW = "http://www.example.com";
    private const string HTML = "text/html; charset=utf-8";
    private const string SKIPPED_NOTE = "The site's other versions (www or not, http or https) weren't all checked, so duplicate versions of the site weren't looked for.";

    private readonly CrawlFiles _files = new();
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _files.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The baseline: www redirects to the bare domain, keeping the path. Nothing to flag, and the check ran.</summary>
    [Fact]
    public async Task AVersionThatRedirectsPermanentlyIsFine()
    {
        var (path, analysis) = await CrawlAsync(RedirectsHome(301));

        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
        Assert.DoesNotContain(SKIPPED_NOTE, analysis.Notes);

        // Every version was probed: the https:// ones are dead through this proxy, and that's no problem of the site's.
        Assert.Equal(
        [
            $"http://www.example.com/ | {(int)FetchOutcome.Redirect}", $"https://example.com/ | {(int)FetchOutcome.NetworkError}",
            $"https://www.example.com/ | {(int)FetchOutcome.NetworkError}"
        ], TestCrawls.Rows(path, $"SELECT u.url, r.outcome FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.source = {(int)DiscoverySource.SiteVersion}"));
        Assert.DoesNotContain(IssueReader.ReadAll(path), row => row.Issue is "internal-unreachable" or "robots-txt-unreachable-other-origin");
    }

    /// <summary>www answers with its own copy of the home page, links and all. It's flagged, and nothing on it is followed.</summary>
    [Fact]
    public async Task AVersionThatAnswersWithAPageIsADuplicateAndIsNotCrawled()
    {
        var expected = await IssuesAsync(RedirectsHome(301));

        var (path, _) = await CrawlAsync(context => context.Request.Path.Value == "/"
            ? Html(context, "<a href=\"/from-www\">Copy</a>")
            : RedirectsHome(301)(context));

        Assert.Equal(Added(expected, "duplicate-site-version | http://www.example.com/ | Answers HTTP 200 instead of redirecting to http://example.com/ | null"),
            Issues(path));
        Assert.DoesNotContain("www.example.com/from-www", _requests);
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE url LIKE '%/from-www'"));
        Assert.Equal(FetchMode.Check, (FetchMode)TestCrawls.Count(path, $"SELECT fetch_mode FROM urls WHERE url = '{WWW}/'"));
    }

    [Fact]
    public async Task AVersionThatRedirectsTemporarilyIsATemporaryRedirectNotADuplicate()
    {
        var expected = await IssuesAsync(RedirectsHome(301));

        var (path, _) = await CrawlAsync(RedirectsHome(302));

        Assert.Equal(Added(expected, "redirect-temporary | http://www.example.com/ | HTTP 302 | http://example.com/"), Issues(path));
    }

    /// <summary>
    /// www sends its home page elsewhere on www, which answers: still a copy of the site, flagged where visitors enter it.
    /// The page it redirects to isn't crawled either.
    /// </summary>
    [Fact]
    public async Task AVersionWhoseRedirectsEndAtAPageOnAnotherVersionIsADuplicate()
    {
        var expected = await IssuesAsync(RedirectsHome(301));

        var (path, _) = await CrawlAsync(context => context.Request.Path.Value switch
        {
            "/" => Redirect(context, 301, $"{WWW}/home"),
            "/home" => Html(context, "<a href=\"/from-www-home\">Copy</a>"),
            _ => RedirectsHome(301)(context)
        });

        Assert.Equal(Added(expected,
                "duplicate-site-version | http://www.example.com/ | Redirects to http://www.example.com/home, which answers HTTP 200 instead of redirecting to http://example.com/ | http://www.example.com/home"),
            Issues(path));
        Assert.DoesNotContain("www.example.com/from-www-home", _requests);
        Assert.Equal(DiscoverySource.SiteVersion, (DiscoverySource)TestCrawls.Count(path, $"SELECT source FROM urls WHERE url = '{WWW}/home'"));
    }

    /// <summary>
    /// www doesn't answer at all, robots.txt included. That's normal, so the probe reports nothing: no unreachable URL, no
    /// unreadable robots.txt on another of the site's origins.
    /// </summary>
    [Fact]
    public async Task ADeadTwinIsNoProblemOfTheSites()
    {
        var expected = await IssuesAsync(RedirectsHome(301));

        var (path, analysis) = await CrawlAsync(context =>
        {
            context.Abort();
            return Task.CompletedTask;
        });

        Assert.Equal(expected, Issues(path));
        Assert.Equal(FetchOutcome.NetworkError, (FetchOutcome)TestCrawls.Count(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{WWW}/'"));
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM robots WHERE origin = '{WWW}' AND status_code IS NULL"));
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
    }

    /// <summary>
    /// www redirects to a page that's gone. A redirect to a broken URL is reported on the version, but the page isn't the
    /// site's broken URL, until the site links to it.
    /// </summary>
    [Fact]
    public async Task WhereAVersionLeadsBecomesTheSitesOwnOnceTheSiteLinksThere()
    {
        Task Www(HttpContext context) => context.Request.Path.Value switch
        {
            "/" => Redirect(context, 301, $"{WWW}/gone"),
            "/gone" => Status(context, 404),
            _ => RedirectsHome(301)(context)
        };

        var expected = await IssuesAsync(RedirectsHome(301));
        var (probed, _) = await CrawlAsync(Www);
        Assert.Equal(Added(expected, "redirect-broken | http://www.example.com/ | Ends at a URL that returns HTTP 404 | http://www.example.com/gone"),
            Issues(probed));

        var (linked, _) = await CrawlAsync(Www, home: $"<a href=\"/a\">A</a> <a href=\"{WWW}/gone\">Gone</a>");
        var issues = Issues(linked);
        Assert.Contains("internal-client-error | http://www.example.com/gone | HTTP 404 | null", issues);
        Assert.Contains(issues, row => row.StartsWith("broken-internal-links | http://example.com/ |", StringComparison.Ordinal));
        Assert.Equal(DiscoverySource.Link, (DiscoverySource)TestCrawls.Count(linked, $"SELECT source FROM urls WHERE url = '{WWW}/gone'"));
    }

    /// <summary>
    /// The site lives on www/home: the start URL redirects to the www version, which the probe got to first, and that
    /// redirects on. However the probe and the start URL's redirect race, the home page is crawled in full, with its
    /// links, as the site's own.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AVersionTheStartUrlLeadsToIsCrawledAsTheSitesOwn(bool probeAnswersFirst)
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(context => context.Request.Host.Host == "www.example.com"
            ? context.Request.Path.Value switch
            {
                "/" => Redirect(context, 301, $"{WWW}/home"),
                "/home" => Html(context, "<a href=\"/about\">About</a>"),
                "/about" => Html(context, "<p>About us</p>"),
                "/robots.txt" => Text(context, "User-agent: *\nAllow: /"),
                _ => Status(context, 404)
            }
            : Redirect(context, 301, WWW + context.Request.Path));

        // Either the start URL waits until the probe has followed www's redirect, or the probe waits until the start
        // URL's redirect is stored.
        var (held, until) = probeAnswersFirst
            ? ("example.com/", $"SELECT COUNT(*) FROM urls WHERE url = '{WWW}/home' AND state = {(int)UrlState.Done}")
            : ("www.example.com/", $"SELECT COUNT(*) FROM urls WHERE url = '{BARE}/' AND state = {(int)UrlState.Done}");
        Hold(held);
        await using (var job = TestCrawls.Start(Options(site), path))
        {
            await TestCrawls.WaitUntilAsync(path, until);
            Release(held);
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.Equal(2, CrawlStatsReader.Read(path).HtmlPageCount);
        string Row(string url, FetchMode mode, int depth, DiscoverySource source) =>
            $"{url} | 1 | {(int)mode} | {depth} | {(int)UrlState.Done} | {(int)SkipReason.None} | {(int)source}";
        Assert.Equal(
        [
            Row($"{BARE}/", FetchMode.Crawl, 0, DiscoverySource.Seed), Row($"{WWW}/", FetchMode.Crawl, 0, DiscoverySource.Link),
            Row($"{WWW}/about", FetchMode.Crawl, 1, DiscoverySource.Link), Row($"{WWW}/home", FetchMode.Crawl, 0, DiscoverySource.Link),
            Row("https://example.com/", FetchMode.Check, 0, DiscoverySource.SiteVersion), Row("https://www.example.com/", FetchMode.Check, 0, DiscoverySource.SiteVersion)
        ], TestCrawls.Rows(path, CrawlTables.URLS.Replace("skip_reason", "skip_reason, source", StringComparison.Ordinal)));
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
    }

    /// <summary>
    /// The start URL redirects to www, which redirects off the site, so there's nothing to crawl and the crawler says so,
    /// whichever comes first. A probe that leaves the site, though, isn't the start URL leaving.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTheStartUrlLeavingTheSiteIsWarnedAbout(bool probeAnswersFirst)
    {
        await using var leaving = await StartAsync(context => context.Request.Host.Host switch
        {
            "www.example.com" => Redirect(context, 301, "http://elsewhere.example/"),
            "elsewhere.example" => Html(context, "<p>Another site</p>"),
            _ => Redirect(context, 301, WWW + context.Request.Path)
        });
        var observer = new RecordingObserver();
        var path = _files.NewPath();
        var (held, until) = probeAnswersFirst
            ? ("example.com/", $"SELECT COUNT(*) FROM urls WHERE url = '{WWW}/' AND state = {(int)UrlState.Done}")
            : ("www.example.com/", $"SELECT COUNT(*) FROM urls WHERE url = '{BARE}/' AND state = {(int)UrlState.Done}");
        Hold(held);
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(Options(leaving), path), observer))
        {
            await TestCrawls.WaitUntilAsync(path, until);
            Release(held);
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        Assert.Equal(1, observer.Count("start-redirects-away"));

        // The site itself answers; only its www version goes elsewhere.
        var quiet = new RecordingObserver();
        await using var staying = await StartAsync(SiteWith(context => Redirect(context, 301, "http://elsewhere.example/")));
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(Options(staying), _files.NewPath()), quiet))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        Assert.Equal(0, quiet.Count("start-redirects-away"));
    }

    /// <summary>
    /// A URL limit of one: the home page uses it, and the versions are probed all the same, as is where www redirects,
    /// found once the limit is reached.
    /// </summary>
    [Fact]
    public async Task TheUrlLimitDoesNotKeepTheProbesOut()
    {
        var (path, _) = await CrawlAsync(context => context.Request.Path.Value switch
        {
            "/" => Redirect(context, 301, $"{WWW}/home"),
            "/home" => Html(context, "<p>A copy</p>"),
            _ => RedirectsHome(301)(context)
        }, options => options with { MaxUrls = 1 });

        Assert.Equal(["duplicate-site-version | http://www.example.com/ | Redirects to http://www.example.com/home, which answers HTTP 200 instead of redirecting to http://example.com/ | http://www.example.com/home"],
            Issues(path).Where(row => row.StartsWith("duplicate-site-version", StringComparison.Ordinal)));
        Assert.Equal(1, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE state = {(int)UrlState.Done} AND source <> {(int)DiscoverySource.SiteVersion}"));
        Assert.Equal(4, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE state = {(int)UrlState.Done} AND source = {(int)DiscoverySource.SiteVersion}"));
    }

    /// <summary>
    /// The origins whose robots.txt the analysis leaves out are those only probes went to: not one the site also links
    /// to, and not one the crawl has no URLs on at all.
    /// </summary>
    [Fact]
    public async Task OnlyOriginsNothingButProbesWentToAreLeftOutOfTheRobotsTxtChecks()
    {
        Task Www(HttpContext context) => context.Request.Path.Value == "/" ? Redirect(context, 301, $"{WWW}/gone") : Status(context, 404);
        var (probed, _) = await CrawlAsync(Www);
        var (linked, _) = await CrawlAsync(Www, home: $"<a href=\"{WWW}/gone\">Gone</a>");
        foreach (var path in new[] { probed, linked })
        {
            using var connection = CrawlDatabase.OpenWriter(path);
            CrawlDatabase.Execute(connection, "INSERT INTO robots (origin, status_code, fetched_at) VALUES ('http://example.com:8080', 503, 0)");
        }

        HashSet<string> Origins(string path)
        {
            using var reader = AnalysisReader.Open(path);
            return reader.ReadSiteVersionOnlyOrigins();
        }

        Assert.Equal([WWW, "https://example.com", "https://www.example.com"], Origins(probed).Order(StringComparer.Ordinal));
        Assert.Equal(["https://example.com", "https://www.example.com"], Origins(linked).Order(StringComparer.Ordinal));
    }

    /// <summary>A crawl stopped while www was still being asked doesn't know how it answers, so the check is skipped, not passed.</summary>
    [Fact]
    public async Task AStoppedCrawlWhoseProbesHaventAnsweredSkipsTheCheck()
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(SiteWith(context => Html(context, "<p>A copy</p>")));
        Hold("www.example.com/");
        await using (var job = TestCrawls.Start(Options(site), path))
        {
            await TestCrawls.WaitUntilAsync(path, $"SELECT COUNT(*) FROM urls WHERE url = '{BARE}/a' AND state = {(int)UrlState.Done}");
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        AssertSkipped(path, analysis);
    }

    /// <summary>
    /// A crawl file from before the crawler probed: the check is skipped with a note, and nothing is stored for it. Resumed,
    /// the crawl probes the versions, and the check runs.
    /// </summary>
    [Fact]
    public async Task ACrawlFromBeforeTheProbesGetsThemWhenResumed()
    {
        var (path, analysis) = await CrawlAsync(context => Html(context, "<p>A copy</p>"));
        Assert.Equal(1, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            var probes = $"SELECT id FROM urls WHERE source = {(int)DiscoverySource.SiteVersion}";
            CrawlDatabase.Execute(connection, $"""
                DELETE FROM responses WHERE url_id IN ({probes});
                DELETE FROM response_details WHERE url_id IN ({probes});
                DELETE FROM links WHERE from_id IN ({probes}) OR to_id IN ({probes});
                DELETE FROM robots WHERE origin <> '{BARE}';
                DELETE FROM urls WHERE source = {(int)DiscoverySource.SiteVersion};
                """);
        }

        AssertSkipped(path, await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));

        await using var site = await StartAsync(SiteWith(context => Html(context, "<p>A copy</p>")));
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(site), path, resume: true)).Status);
        var resumed = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.DoesNotContain(SKIPPED_NOTE, resumed.Notes);
        Assert.Equal(["duplicate-site-version | http://www.example.com/ | Answers HTTP 200 instead of redirecting to http://example.com/ | null"],
            Issues(path).Where(row => row.StartsWith("duplicate-site-version", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Stopped once every version has answered (the https:// ones dead, recorded at once), then resumed: www isn't probed
    /// again, and no version is stored twice.
    /// </summary>
    [Fact]
    public async Task AResumedCrawlDoesNotProbeAgain()
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(SiteWith(context => Html(context, "<p>A copy</p>")));
        Hold("example.com/a");
        await using (var job = TestCrawls.Start(Options(site), path))
        {
            await TestCrawls.WaitUntilAsync(path, $"""
                SELECT COUNT(*) = 3 FROM urls WHERE source = {(int)DiscoverySource.SiteVersion} AND state = {(int)UrlState.Done}
                """);
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        Release("example.com/a");
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(site), path, resume: true)).Status);

        Assert.Single(_requests, request => request == "www.example.com/");
        Assert.Equal(3, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE source = {(int)DiscoverySource.SiteVersion}"));
        Assert.Equal(FetchOutcome.Success, (FetchOutcome)TestCrawls.Count(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{BARE}/a'"));
    }

    /// <summary>
    /// A URL limit of three, reached by the home page and the two pages claimed next, one of which is still loading when
    /// the crawl stops. Resumed, that page gets the slot it had: www, probed and answered by then, took none.
    /// </summary>
    [Fact]
    public async Task AResumedCrawlDoesNotCountTheProbesTowardsTheUrlLimit()
    {
        var path = _files.NewPath();
        var home = string.Concat("abcde".Select(page => $"<a href=\"/{page}\">{page}</a> "));
        await using var site = await StartAsync(SiteWith(context => Html(context, "<p>A copy</p>"), home));
        var options = Options(site) with { MaxUrls = 3 };
        Hold("example.com/a");
        await using (var job = TestCrawls.Start(options, path))
        {
            await TestCrawls.WaitUntilAsync(path, $"""
                SELECT COUNT(*) = 2 FROM urls WHERE url IN ('{WWW}/', '{BARE}/b') AND state = {(int)UrlState.Done}
                """);
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        Release("example.com/a");
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path, resume: true)).Status);

        Assert.Equal(FetchOutcome.Success, (FetchOutcome)TestCrawls.Count(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{BARE}/a'"));
        Assert.Equal(3, TestCrawls.Count(path, $"""
            SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state <> {(int)UrlState.Skipped} AND source <> {(int)DiscoverySource.SiteVersion}
            """));
    }

    /// <summary>
    /// www accepts the connection and never answers, as a port behind a firewall that drops packets does once connected.
    /// Its robots.txt is fetched as ever, three tries with backoff, but the probe isn't sent after that: it is recorded
    /// dead with robots.txt's timeout, and nothing is tried again at the end. So the crawl, whose own pages take moments,
    /// takes one robots.txt cycle, not that plus the probe's own tries plus one more at the end.
    /// </summary>
    [Fact]
    public async Task AVersionThatNeverAnswersCostsOneRobotsTxtCycle()
    {
        const int TIMEOUT_SECONDS = 2;
        var path = _files.NewPath();
        await using var site = await StartAsync(SiteWith(NeverAnswersAsync));
        var options = Options(site) with { RequestTimeoutSeconds = TIMEOUT_SECONDS, MaxRetries = 2 };

        var clock = Stopwatch.StartNew();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        clock.Stop();
        output.WriteLine($"Crawl with a version that never answers: {clock.Elapsed.TotalSeconds:0.0} s");

        // Three tries of robots.txt, 1 s and 2 s apart (each with up to 0.25 s of jitter).
        var robotsCycle = TimeSpan.FromSeconds((options.MaxRetries + 1) * TIMEOUT_SECONDS + 1 + 2 + 0.5);
        Assert.True(clock.Elapsed < robotsCycle + TimeSpan.FromSeconds(3), $"The crawl took {clock.Elapsed}, a robots.txt cycle {robotsCycle}.");
        Assert.Equal(3, _requests.Count(request => request == "www.example.com/robots.txt"));
        Assert.DoesNotContain("www.example.com/", _requests);
        Assert.Equal([$"{(int)FetchOutcome.Timeout} | yes"], TestCrawls.Rows(path, $"""
            SELECT r.outcome, CASE WHEN r.error = (SELECT error FROM robots WHERE origin = '{WWW}') THEN 'yes' ELSE r.error END
            FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{WWW}/'
            """));

        // Dead, so neither flagged nor skipped, and nothing else reported for it.
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
        Assert.DoesNotContain(IssueReader.ReadAll(path), row => row.Issue is "internal-unreachable" or "robots-txt-unreachable-other-origin");
    }

    /// <summary>www's robots.txt answers but its page never does: the probe is asked once, with no retries and no try at the end.</summary>
    [Fact]
    public async Task AVersionWhosePageNeverAnswersIsAskedOnce()
    {
        const int TIMEOUT_SECONDS = 2;
        var path = _files.NewPath();
        await using var site = await StartAsync(SiteWith(context => context.Request.Path.Value == "/robots.txt" ? Status(context, 404) : NeverAnswersAsync(context)));
        var options = Options(site) with { RequestTimeoutSeconds = TIMEOUT_SECONDS, MaxRetries = 2 };

        var clock = Stopwatch.StartNew();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        clock.Stop();
        output.WriteLine($"Crawl with a version whose page never answers: {clock.Elapsed.TotalSeconds:0.0} s");

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(TIMEOUT_SECONDS + 4), $"The crawl took {clock.Elapsed}.");
        Assert.Single(_requests, request => request == "www.example.com/");
        Assert.Equal(FetchOutcome.Timeout, (FetchOutcome)TestCrawls.Count(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{WWW}/'"));
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(0, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION).Count);
    }

    /// <summary>
    /// A site on an internationalised domain, started from its Unicode name, with a Unicode Sitemap line and links. Stored
    /// URLs and robots.txt origins are punycode alike, so every origin comparison sees one form: www, answering with a
    /// page, is flagged; the robots.txt of the dead https:// versions and www's missing one, which only probes went to,
    /// aren't reported; and the sitemap's entry is on the site's own host.
    /// </summary>
    [Fact]
    public async Task AnInternationalisedDomainHasItsOriginsInOneForm()
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(Idn(robots: context => Text(context, $"User-agent: *\nAllow: /\nSitemap: {UNICODE}/sitemap.xml")));
        var options = new CrawlOptions { StartUrl = $"{UNICODE}/", ProxyUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 };

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path)).Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.Equal([$"http://www.{PUNYCODE[7..]}", PUNYCODE, $"https://www.{PUNYCODE[7..]}", $"https://{PUNYCODE[7..]}"],
            TestCrawls.Rows(path, "SELECT origin FROM robots"));
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM urls WHERE url LIKE '%{UNICODE[7..]}%'"));
        var issues = Issues(path);
        Assert.Contains($"duplicate-site-version | http://www.{PUNYCODE[7..]}/ | Answers HTTP 200 instead of redirecting to {PUNYCODE}/ | null", issues);
        Assert.DoesNotContain(issues, row => row.Split(" | ")[0] is "robots-txt-unreachable-other-origin" or "robots-txt-missing" or "sitemap-url-other-host");
    }

    /// <summary>
    /// The same site's robots.txt fails (HTTP 503) on the main origin itself, which the analysis must call the main
    /// robots.txt being unavailable, not another origin's. On the fake clock, so its two-minute cool-down passes at once.
    /// </summary>
    [Fact]
    public async Task AnInternationalisedDomainsMainRobotsTxtIsTheMainOne()
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(Idn(robots: context => Status(context, 503)));
        var options = new CrawlOptions { StartUrl = $"{UNICODE}/", ProxyUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };
        var time = new FakeTimeProvider();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await FakeClock.PumpAsync(time, job.Completion)).Status);
        }

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var issues = Issues(path);
        Assert.Contains($"robots-txt-unreachable | {PUNYCODE}/ | {PUNYCODE}/robots.txt (HTTP 503) | null", issues);
        Assert.DoesNotContain(issues, row => row.StartsWith("robots-txt-unreachable-other-origin", StringComparison.Ordinal));
    }

    private const string UNICODE = "http://bücher.example";
    private const string PUNYCODE = "http://xn--bcher-kva.example";

    /// <summary>bücher.example with a home page, one page and a sitemap listing it, and its www version answering with a copy.</summary>
    private static RequestDelegate Idn(RequestDelegate robots)
    {
        var idn = new IdnMapping();
        return context => idn.GetAscii(context.Request.Host.Host) switch
        {
            "www.xn--bcher-kva.example" => context.Request.Path.Value == "/robots.txt" ? Status(context, 404) : Html(context, "<p>A copy</p>"),
            "xn--bcher-kva.example" => context.Request.Path.Value switch
            {
                "/robots.txt" => robots(context),
                "/sitemap.xml" => Xml(context, $"<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{UNICODE}/a</loc></url></urlset>"),
                "/" => Html(context, $"<a href=\"{UNICODE}/a\">A</a>"),
                "/a" => Html(context, "<p>A page</p>"),
                _ => Status(context, 404)
            },
            _ => Status(context, 404)
        };
    }

    private static Task Xml(HttpContext context, string body)
    {
        context.Response.ContentType = "application/xml";
        return context.Response.WriteAsync(body);
    }

    private static async Task NeverAnswersAsync(HttpContext context)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The crawler gave up waiting.
        }
    }

    private static void AssertSkipped(string path, AnalysisSummary analysis)
    {
        Assert.Contains(SKIPPED_NOTE, analysis.Notes);
        Assert.True(analysis.IsPartial);
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.DUPLICATE_SITE_VERSION);
        Assert.False(IssueReader.ReadCounts(path).ContainsKey(IssueCatalog.DUPLICATE_SITE_VERSION.Id));
        Assert.Empty(IssueReader.ReadAll(path, IssueCatalog.DUPLICATE_SITE_VERSION.Id));
    }

    /// <summary>Crawls the site with <paramref name="www"/> answering for the www host, and analyses it.</summary>
    private async Task<(string Path, AnalysisSummary Analysis)> CrawlAsync(RequestDelegate www, Func<CrawlOptions, CrawlOptions>? configure = null,
        string home = "<a href=\"/a\">A</a>")
    {
        var path = _files.NewPath();
        await using var site = await StartAsync(SiteWith(www, home));
        var options = Options(site);
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(configure?.Invoke(options) ?? options, path)).Status);
        return (path, await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));
    }

    private async Task<List<string>> IssuesAsync(RequestDelegate www) => Issues((await CrawlAsync(www)).Path);

    /// <summary>Every stored issue as "issue | url | detail | related url", sorted.</summary>
    private static List<string> Issues(string path) => TestCrawls.Rows(path, """
        SELECT i.issue, u.url, COALESCE(i.detail, 'null'), COALESCE(related.url, 'null')
        FROM issues i JOIN urls u ON u.id = i.url_id LEFT JOIN urls related ON related.id = i.related_url_id
        """);

    private static List<string> Added(List<string> issues, params string[] rows) => [.. issues.Concat(rows).Order(StringComparer.Ordinal)];

    private static CrawlOptions Options(LoopbackServer site) =>
        new() { StartUrl = $"{BARE}/", ProxyUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };

    /// <summary>The site on the bare domain: a home page and one more page. <paramref name="www"/> answers for its www version.</summary>
    private static RequestDelegate SiteWith(RequestDelegate www, string home = "<a href=\"/a\">A</a>") => context =>
        context.Request.Host.Host switch
        {
            "www.example.com" => www(context),
            "example.com" => context.Request.Path.Value switch
            {
                "/robots.txt" => Text(context, "User-agent: *\nAllow: /"),
                "/" => Html(context, home),
                "/a" => Html(context, "<p>A page</p>"),
                _ => Status(context, 404)
            },
            _ => Status(context, 404)
        };

    /// <summary>www redirecting to the same path on the bare domain, robots.txt included, as a well-run site's does.</summary>
    private static RequestDelegate RedirectsHome(int status) => context => Redirect(context, status, BARE + context.Request.Path + context.Request.QueryString);

    /// <summary>
    /// The site as the crawl's proxy, which logs every request as "host/path" and holds back the ones a test holds. https
    /// goes through a proxy as a CONNECT tunnel, which this one refuses: the https:// versions are dead.
    /// </summary>
    private Task<LoopbackServer> StartAsync(RequestDelegate site) => LoopbackServer.StartAsync(async context =>
    {
        if (HttpMethods.IsConnect(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        var request = context.Request.Host.Host + context.Request.Path;
        _requests.Enqueue(request);
        if (_held.TryGetValue(request, out var hold))
        {
            try
            {
                await hold.Task.WaitAsync(context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        await site(context);
    });

    private void Hold(string request) => _held[request] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Release(string request)
    {
        if (_held.TryRemove(request, out var hold))
        {
            hold.TrySetResult();
        }
    }

    private static Task Html(HttpContext context, string body)
    {
        context.Response.ContentType = HTML;
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><title>Test page</title></head><body>{body}</body></html>");
    }

    private static Task Text(HttpContext context, string body)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(body);
    }

    private static Task Redirect(HttpContext context, int status, string location)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }

    private static Task Status(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
