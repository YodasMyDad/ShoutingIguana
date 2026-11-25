using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A host that answers plain HTTP with a Cloudflare challenge is read in a browser when the crawl asks for it, and
/// that HTML is parsed once. The next page on the host is not fetched by HTTP. A challenge the browser also gets
/// stays a block, and the host stays on HTTP. Turned off, the challenge is left as <c>internal-blocked</c>.
/// </summary>
public sealed class BrowserFallbackCrawlTests : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2, Sitemaps = false });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ABlockedHostIsReadInTheBrowserAndTheNextPageSkipsHttp()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_PATH).AbsoluteUri;
        var next = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();

        var summary = await RunAsync(path, start, browserFallback: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal(((int)FetchOutcome.Success).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.Equal("200", Scalar(path, next, "r.status_code"));

        var page = Hits(hits, SyntheticSite.BROWSER_FALLBACK_PATH);
        Assert.Contains(page, hit => !hit.FromBrowser);
        Assert.Contains(page, hit => hit.FromBrowser);
        Assert.DoesNotContain(Hits(hits, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH), hit => !hit.FromBrowser);
        Assert.Contains(Hits(hits, SyntheticSite.BROWSER_FALLBACK_NEXT_PATH), hit => hit.FromBrowser);

        Assert.Equal("browser-fallback", Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'")));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path)[IssueCatalog.TITLE_MISSING.Id]);
        Assert.EndsWith(SyntheticSite.BROWSER_FALLBACK_PATH, Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.TITLE_MISSING.Id, 10)).Url);
        Assert.Equal(0, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.INTERNAL_BLOCKED.Id));
    }

    [Fact]
    public async Task OffLeavesTheChallengeBlocked()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.BROWSER_FALLBACK_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();

        var summary = await RunAsync(path, start, browserFallback: false);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("Cloudflare challenge", Scalar(path, start, "r.block_reason"));
        Assert.DoesNotContain(TestCrawls.Rows(path, "SELECT url FROM urls"), url => url.Contains("browser-fallback-next", StringComparison.Ordinal));
        Assert.DoesNotContain(Hits(hits, SyntheticSite.BROWSER_FALLBACK_PATH), hit => hit.FromBrowser);
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));

        // The alert names the setting that might read the site, by the name it has in the app.
        var alert = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'blocked'"));
        Assert.Contains("turn on \"If a site blocks the check\" (\"Browser fallback\" in Advanced) and crawl again", alert, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChallengeTheBrowserAlsoGetsDoesNotSwitchTheHost()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, "/plants/blocked").AbsoluteUri;
        var summary = await RunAsync(path, start, browserFallback: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("Cloudflare challenge", Scalar(path, start, "r.block_reason"));
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));

        // The setting is on already, so the alert doesn't send anyone to it. It says what the site's owner can do.
        var alert = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'blocked'"));
        Assert.DoesNotContain("turn on", alert, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(PageProcessor.OWNER_ADVICE, alert, StringComparison.Ordinal);
    }

    /// <summary>
    /// A check that clears by itself is passed in the computer's own Chrome or Edge, which sends its own user agent,
    /// and the host's next pages are read there: a redirect as a redirect, to be followed.
    /// </summary>
    [Fact]
    public async Task AnAutomaticCheckClearsInTheInstalledBrowser()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_PATH).AbsoluteUri;
        var next = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_NEXT_PATH).AbsoluteUri;
        var last = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_LAST_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();

        // One request at a time: the page answers the pass with a redirect to itself, and that second ask must not
        // wait for the first one's turn on the host.
        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true, concurrency: 1);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal("Human check passed | Synthetic Store planted page", Scalar(path, start, "r.title"));
        Assert.Equal("301", Scalar(path, next, "r.status_code"));
        Assert.Equal(((int)FetchOutcome.Redirect).ToString(), Scalar(path, next, "r.outcome"));
        Assert.Equal("200", Scalar(path, last, "r.status_code"));
        Assert.Equal("Human check passed, last page | Synthetic Store planted page", Scalar(path, last, "r.title"));

        // The challenge is never stored as the page, and once the browser reads the host, plain HTTP is not sent again.
        Assert.Empty(TestCrawls.Rows(path, "SELECT title FROM responses WHERE title LIKE 'Just a moment%'"));
        Assert.DoesNotContain(Hits(hits, SyntheticSite.HUMAN_CHECK_NEXT_PATH), hit => !hit.FromBrowser);
        Assert.DoesNotContain(Hits(hits, SyntheticSite.HUMAN_CHECK_LAST_PATH), hit => !hit.FromBrowser);

        var said = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'browser-fallback'"));
        Assert.Contains("as itself: it sends its own user agent, not the crawler's", said, StringComparison.Ordinal);
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code IN ('blocked', 'human-check')"));
    }

    /// <summary>The headless browser says it is automated, so the same check never clears in it.</summary>
    [Fact]
    public async Task AnAutomaticCheckStaysBlockedInTheHeadlessBrowser()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_PATH).AbsoluteUri;

        var summary = await RunAsync(path, start, browserFallback: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        var alert = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'blocked'"));
        Assert.Contains("told not to use the computer's own Chrome or Edge", alert, StringComparison.Ordinal);
        Assert.EndsWith(PageProcessor.OWNER_ADVICE, alert, StringComparison.Ordinal);
    }

    /// <summary>A check that needs a person is a block when the crawl was not told to ask one, and the alert says how to be asked.</summary>
    [Fact]
    public async Task ACheckThatNeedsAPersonIsBlockedWhenNobodyIsAsked()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_ASK_PATH).AbsoluteUri;

        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("Cloudflare challenge", Scalar(path, start, "r.block_reason"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code IN ('browser-fallback', 'human-check')"));
        var alert = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'blocked'"));
        Assert.Contains("is asking to check you're human (Cloudflare challenge), and that needs a person", alert, StringComparison.Ordinal);
        Assert.Contains("crawl again with --solve-challenges", alert, StringComparison.Ordinal);
    }

    /// <summary>
    /// Told to ask, the crawl shows the window on the page and says so, waits, and goes on with the same browser once
    /// the check is completed. The planted check completes itself when its window is on the screen, as the person would.
    /// </summary>
    [Fact]
    public async Task ACheckAPersonCompletesLetsTheCrawlGoOn()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_ASK_PATH + "?shown=1").AbsoluteUri;
        var last = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_LAST_PATH).AbsoluteUri;
        var observer = new WaitObserver();

        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true, solveChallenges: true, observer: observer);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("200", Scalar(path, start, "r.status_code"));
        Assert.Equal("Human check passed | Synthetic Store planted page", Scalar(path, start, "r.title"));
        Assert.Equal("200", Scalar(path, last, "r.status_code"));

        var asked = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'human-check'"));
        Assert.Contains("asked to check you're human (Cloudflare challenge)", asked, StringComparison.Ordinal);
        Assert.Contains("window opened on the page for you to complete the check", asked, StringComparison.Ordinal);
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'blocked'"));
        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'browser-fallback'"));

        // How the check ended is said too, with the browser it was completed in.
        var passed = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'human-check-passed'"));
        Assert.Contains("check was completed. The crawl is reading the site in", passed, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a real site did (indeed.com, 2026-11-24): the person ticked the box, the page loaded again, and the site
    /// answered that load with a redirect to its pages for the visitor's country. The page was on the screen and the
    /// crawl stored the address as blocked. The redirect is the address's answer: it is stored, and followed in the
    /// same browser, with the pass, through the host's next pages. The log says each step, for a person to read back.
    /// </summary>
    [Fact]
    public async Task ARedirectAfterACheckAPersonCompletesIsFollowedInTheSameBrowser()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_MOVED_PATH + "?shown=1").AbsoluteUri;
        var next = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_NEXT_PATH).AbsoluteUri;
        var last = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_LAST_PATH).AbsoluteUri;
        var hits = _server.Requests.WatchHits();
        using var log = new CapturingLoggerFactory();

        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true, solveChallenges: true, log: log);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("302", Scalar(path, start, "r.status_code"));
        Assert.Equal(((int)FetchOutcome.Redirect).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.block_reason, '')"));
        Assert.Equal("301", Scalar(path, next, "r.status_code"));
        Assert.Equal("200", Scalar(path, last, "r.status_code"));
        Assert.Equal("Human check passed, last page | Synthetic Store planted page", Scalar(path, last, "r.title"));
        Assert.DoesNotContain(Hits(hits, SyntheticSite.HUMAN_CHECK_NEXT_PATH), hit => !hit.FromBrowser);
        Assert.DoesNotContain(Hits(hits, SyntheticSite.HUMAN_CHECK_LAST_PATH), hit => !hit.FromBrowser);

        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'human-check'"));
        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'human-check-passed'"));
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code IN ('blocked', 'start-redirects-away')"));

        // The window the person was shown was whole on the main screen, restored, and the log has every step in order.
        var said = log.Messages.Where(message => message.StartsWith("Information ", StringComparison.Ordinal)).ToList();
        var steps = new[]
        {
            "Human check: 127.0.0.1 asks for a person (Cloudflare challenge)",
            "window is shown at",
            "Human check: 127.0.0.1's check was completed after",
            "127.0.0.1's check cleared, and " + start + " answered 302, a redirect to " + next,
            "Read " + next + " in",
            "Read " + last + " in",
            "pages of 127.0.0.1 were read in the computer's own browser"
        };
        var at = -1;
        foreach (var step in steps)
        {
            var found = said.FindIndex(at + 1, message => message.Contains(step, StringComparison.Ordinal));
            Assert.True(found > at, $"The log doesn't say \"{step}\" after step {at}:\n{string.Join('\n', said)}");
            at = found;
        }

        var shown = said.Single(message => message.Contains("window is shown at", StringComparison.Ordinal));
        Assert.Contains("normal", shown, StringComparison.Ordinal);
        Assert.Contains("whole on the screen: True", shown, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Messages, message => message.Contains(SyntheticSite.HUMAN_CHECK_ASK_COOKIE, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same, where the redirect leads to another site, as indeed.com's did (to uk.indeed.com): the crawl stores
    /// the redirect and says there is nothing more to crawl from here and where to start instead. Not a block.
    /// </summary>
    [Fact]
    public async Task ARedirectToAnotherSiteAfterACheckIsSaidNotBlocked()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_MOVED_PATH + "?shown=1&away=1").AbsoluteUri;

        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true, solveChallenges: true);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal("302", Scalar(path, start, "r.status_code"));
        Assert.Equal(((int)FetchOutcome.Redirect).ToString(), Scalar(path, start, "r.outcome"));
        var away = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'start-redirects-away'"));
        Assert.Contains($"http://localhost:{_server.BaseUri.Port}{SyntheticSite.HUMAN_CHECK_LAST_PATH}", away, StringComparison.Ordinal);
        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'human-check-passed'"));
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'blocked'"));
    }

    /// <summary>A check nobody completes in the time given is a block, the alert says what happened, and the host is not asked about again.</summary>
    [Fact]
    public async Task ACheckNobodyCompletesStaysBlocked()
    {
        if (!CanOpenABrowser())
        {
            return;
        }

        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.HUMAN_CHECK_ASK_PATH).AbsoluteUri;
        var observer = new WaitObserver();

        var summary = await RunAsync(path, start, browserFallback: true, useInstalledBrowser: true, solveChallenges: true, challengeWaitSeconds: 3,
            observer: observer);
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        Assert.Equal(((int)FetchOutcome.Blocked).ToString(), Scalar(path, start, "r.outcome"));
        Assert.Equal("", Scalar(path, start, "COALESCE(r.title, '')"));
        Assert.Single(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'human-check'"));
        var alert = Assert.Single(TestCrawls.Rows(path, "SELECT message FROM notices WHERE code = 'blocked'"));
        Assert.Contains("the check was not completed in the browser window", alert, StringComparison.Ordinal);

        // The alert's facts say what the browser fallback did, for the app to put in its own words.
        var facts = Assert.Single(TestCrawls.Rows(path, "SELECT data FROM notices WHERE code = 'blocked'"));
        Assert.Contains("\"tried\":\"notCompleted\"", facts, StringComparison.Ordinal);
        Assert.Empty(TestCrawls.Rows(path, "SELECT code FROM notices WHERE code = 'human-check-passed'"));

        // While the window was up, progress said the crawl was waiting for a person, and for which host.
        var wait = Assert.Single(observer.HumanChecks.DistinctBy(check => check.Host));
        Assert.Equal("127.0.0.1", wait.Host);
    }

    /// <summary>These need a Chrome, Edge or Chromium on the computer, and a screen. A build server may have neither.</summary>
    private static bool CanOpenABrowser() => InstalledBrowsers.Find() is not null && InstalledBrowsers.HasDisplay();

    private async Task<CrawlSummary> RunAsync(string path, string start, bool browserFallback, bool useInstalledBrowser = false,
        bool solveChallenges = false, int challengeWaitSeconds = 60, ICrawlObserver? observer = null, int concurrency = 2,
        Microsoft.Extensions.Logging.ILoggerFactory? log = null)
    {
        var options = new CrawlOptions
        {
            StartUrl = start,
            Concurrency = concurrency,
            Rendering = JavaScriptRendering.Off,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false,
            BrowserFallback = browserFallback,
            UseInstalledBrowser = useInstalledBrowser,
            SolveChallenges = solveChallenges,
            ChallengeWaitSeconds = challengeWaitSeconds
        };
        await using var job = new CrawlEngine(log ?? NullLoggerFactory.Instance).Start(new CrawlRequest(options, path), observer);
        return await job.Completion.WaitAsync(TimeSpan.FromSeconds(120));
    }

    private static string Scalar(string path, string url, string column) =>
        Assert.Single(TestCrawls.Rows(path, $"SELECT {column} FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url = '{url}'"));

    private static List<SyntheticHit> Hits(IEnumerable<SyntheticHit> hits, string path) =>
        hits.Where(hit => hit.Path == path).ToList();

    /// <summary>Keeps what progress said the crawl was waiting for a person about.</summary>
    private sealed class WaitObserver : ICrawlObserver
    {
        private readonly List<CrawlWait> _humanChecks = [];

        public IReadOnlyList<CrawlWait> HumanChecks
        {
            get
            {
                lock (_humanChecks)
                {
                    return [.. _humanChecks];
                }
            }
        }

        public void OnProgress(CrawlProgress progress)
        {
            lock (_humanChecks)
            {
                _humanChecks.AddRange(progress.Waits.Where(wait => wait.Reason == CrawlWaitReason.HumanCheck));
            }
        }

        public void OnNotice(CrawlNotice notice)
        {
        }
    }

    private string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-fallback-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }
}
