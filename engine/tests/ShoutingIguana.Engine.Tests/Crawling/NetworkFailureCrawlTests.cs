using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Robots;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// Whole crawls and analyses of small loopback sites whose robots.txt or links fail. Crawls that wait (pauses, cool-downs,
/// giving up) run on the fake clock.
/// </summary>
public sealed class NetworkFailureCrawlTests : IAsyncLifetime
{
    private static readonly TimeSpan CRAWL_TIMEOUT = TimeSpan.FromMinutes(1);

    /// <summary>Fake time per pump step for the crawls that wait out a site for ten minutes.</summary>
    private static readonly TimeSpan LONG_STEP = TimeSpan.FromSeconds(5);

    private readonly List<string> _databases = [];
    private LoopbackServer? _server;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ALinkToASiteThatDoesNotAnswerIsABrokenExternalLink()
    {
        var dead = RobotsCacheHarness.DeadSite();
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/" => Html(context, $"<a href=\"{dead}gone\">Partner</a> <a href=\"/private/x\">Private</a>"),
            "/robots.txt" => Text(context, "User-agent: *\nDisallow: /private/"),
            _ => Status(context, 404)
        });
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.NetworkError, Outcome(path, $"{dead}gone"));
        Assert.Equal(0, observer.Count("robots-unreachable"));

        // A rule's refusal is stored without an error.
        Assert.Equal(FetchOutcome.RobotsDisallowed, Outcome(path, $"{_server.BaseUri}private/x"));
        Assert.Null(Error(path, $"{_server.BaseUri}private/x"));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(1, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.BROKEN_EXTERNAL_LINKS.Id));
        Assert.Null(Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BLOCKED_BY_ROBOTS.Id, 10)).Detail);
    }

    [Fact]
    public async Task UrlsRefusedBecauseRobotsTxtIsUnreachableSayWhy()
    {
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value == "/robots.txt"
            ? Status(context, 503)
            : Html(context, "<p>Home</p>"));
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        // Asked again once the cool-down is over, still 503.
        var summary = await CrawlOnFakeClockAsync(Options(), path, observer, new FakeTimeProvider());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.RobotsDisallowed, Outcome(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal($"{RobotsTxt.UNREACHABLE_ERROR}: HTTP 503", Error(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(1, observer.Count("robots-unreachable"));

        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        var issues = IssueReader.ReadCounts(path);
        Assert.Equal(1, issues.GetValueOrDefault(IssueCatalog.ROBOTS_TXT_UNREACHABLE.Id));
        Assert.Equal(1, issues.GetValueOrDefault(IssueCatalog.BLOCKED_BY_ROBOTS.Id));

        // The blocked URL says it wasn't a robots.txt rule, the status by its name: the crawl file keeps its own words.
        var blocked = Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BLOCKED_BY_ROBOTS.Id, 10));
        Assert.Equal($"{RobotsTxt.UNREACHABLE_ERROR}: 503 Service Unavailable", blocked.Detail);
    }

    [Fact]
    public async Task UrlsNotCheckedBecauseTheirSiteStoppedAnsweringAreNotCalledBroken()
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Connection.LocalPort == _server!.OtherBaseUri.Port)
            {
                // The partner site has gone: no answer to anything.
                context.Abort();
                return Task.CompletedTask;
            }

            return context.Request.Path.Value == "/"
                ? Html(context, string.Concat(Enumerable.Range(0, 30).Select(page => $"<a href=\"{_server.OtherBaseUri}p{page}\">Partner {page}</a> ")))
                : Status(context, 404);
        });
        var path = NewDatabasePath();

        await CrawlAsync(path, new RecordingObserver());
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        // The URLs it asked for before the breaker tripped are broken links. The rest weren't asked for, and say so.
        var notChecked = Convert.ToInt32(Scalar(path, $"SELECT COUNT(*) FROM responses WHERE error LIKE '{RetryingFetcher.NOT_CHECKED_ERROR}%'"), CultureInfo.InvariantCulture);
        var failed = Convert.ToInt32(Scalar(path, $"""
            SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 0 AND r.outcome = {(int)FetchOutcome.NetworkError}
            """), CultureInfo.InvariantCulture) - notChecked;
        Assert.True(notChecked > 0);
        Assert.Equal(30, failed + notChecked);
        Assert.StartsWith($"{failed} broken URL", Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.BROKEN_EXTERNAL_LINKS.Id, 10)).Detail);
        Assert.Contains(analysis.Notes, note => note.StartsWith($"{notChecked} URLs weren't checked", StringComparison.Ordinal));

        // The URLs grid shows them as the analysis counts them: unknown, not unreachable.
        var partner = UrlSummaryReader.ReadAll(path).Where(row => row is { IsInternal: false, Inlinks: > 0 }).ToList();
        Assert.Equal(30, partner.Count);
        Assert.Equal(notChecked, partner.Count(row => row is { IndexabilityReason: null, IsIndexable: null, ResponseMs: null, FinalStatusCode: null }));
        Assert.Equal(failed, partner.Count(row => row.IndexabilityReason == IndexabilityReason.Unreachable));
    }

    [Fact]
    public async Task ASiteWithoutRobotsTxtIsCrawledInFullAndToldSo()
    {
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/" => Html(context, "<a href=\"/private/x\">Anything goes</a>"),
            "/private/x" => Html(context, "<p>Crawled, with no robots.txt to say otherwise.</p>"),
            _ => Status(context, 404)
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}private/x"));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(_server.BaseUri.AbsoluteUri, Assert.Single(IssueReader.ReadIssues(path, IssueCatalog.ROBOTS_TXT_MISSING.Id, 10)).Url);
        Assert.Equal(0, IssueReader.ReadCounts(path).GetValueOrDefault(IssueCatalog.ROBOTS_TXT_UNREACHABLE.Id));
    }

    [Fact]
    public async Task APageThatGotNoResponseIsTriedOnceMoreAtTheEnd()
    {
        var flakyRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/")
            {
                return Html(context, "<a href=\"/flaky\">Flaky</a>");
            }

            if (context.Request.Path != "/flaky")
            {
                return Status(context, 404);
            }

            if (Interlocked.Increment(ref flakyRequests) == 1)
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return Html(context, "<p>Back again</p>");
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}flaky"));
        Assert.Equal(2, flakyRequests);
    }

    [Fact]
    public async Task TheTryAtTheEndIsASingleAttempt()
    {
        var deadRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path != "/dead")
            {
                return Html(context, "<a href=\"/dead\">Dead</a>");
            }

            Interlocked.Increment(ref deadRequests);
            context.Abort();
            return Task.CompletedTask;
        });
        var path = NewDatabasePath();

        await CrawlAsync(path, new RecordingObserver(), maxRetries: 1);

        // Two attempts in the crawl, one at the end.
        Assert.Equal(FetchOutcome.NetworkError, Outcome(path, $"{_server.BaseUri}dead"));
        Assert.Equal(3, deadRequests);
    }

    [Fact]
    public async Task ARobotsTxtThatFailedIsReadAgainAtTheEnd()
    {
        var robotsRequests = 0;
        _server = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/robots.txt" when Interlocked.Increment(ref robotsRequests) == 1 => Status(context, 503),
            "/robots.txt" => Text(context, "User-agent: *\nDisallow:"),
            "/" => Html(context, "<a href=\"/about\">About</a>"),
            "/about" => Html(context, "<p>About us</p>"),
            _ => Status(context, 404)
        });
        var path = NewDatabasePath();

        var summary = await CrawlOnFakeClockAsync(Options(), path, new RecordingObserver(), new FakeTimeProvider());

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}about"));
        Assert.Equal(2, robotsRequests);
    }

    /// <summary>
    /// robots.txt fails at first, so the start URL is refused and nothing else is known. Its notice promises a re-ask in
    /// two minutes, and that is when the start URL gets its one more try: not a moment after the failure, when a robots.txt
    /// that is down for a while would still be down and the crawl would end empty. The first request fails by count, not
    /// by time: how much fake time the crawl's start-up takes depends on the machine.
    /// </summary>
    [Fact]
    public async Task ARobotsTxtThatFailsAtTheStartIsAskedAgainWhenItsCoolDownEnds()
    {
        var time = new FakeTimeProvider();
        var robotsRequests = new ConcurrentQueue<DateTimeOffset>();
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path == "/robots.txt")
            {
                robotsRequests.Enqueue(time.GetUtcNow());
                return robotsRequests.Count == 1 ? Status(context, 503) : Text(context, "User-agent: *\nDisallow:");
            }

            return context.Request.Path.Value switch
            {
                "/" => Html(context, "<a href=\"/about\">About</a>"),
                "/about" => Html(context, "<p>About us</p>"),
                _ => Status(context, 404)
            };
        });
        var path = NewDatabasePath();

        var summary = await CrawlOnFakeClockAsync(Options(), path, new RecordingObserver(), time);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Success, Outcome(path, _server.BaseUri.AbsoluteUri));
        Assert.Equal(FetchOutcome.Success, Outcome(path, $"{_server.BaseUri}about"));
        var asked = robotsRequests.ToArray();
        Assert.Equal(2, asked.Length);
        Assert.True(asked[1] - asked[0] >= RobotsCache.RETRY_AFTER, $"robots.txt was asked again after {asked[1] - asked[0]}.");
    }

    [Fact]
    public async Task AStartUrlThatRedirectsToAnotherSiteSaysSo()
    {
        // A site that moved to a new domain: the start URL is all there is to crawl, which looks broken unless it's said.
        const string MOVED_TO = "http://moved.invalid/";
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path.Value == "/robots.txt")
            {
                return Status(context, 404);
            }

            context.Response.Headers.Location = MOVED_TO;
            return Status(context, 301);
        });
        var observer = new RecordingObserver();
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, observer);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(FetchOutcome.Redirect, Outcome(path, _server.BaseUri.AbsoluteUri));
        var notice = Assert.Single(observer.Notices, notice => notice.Code == "start-redirects-away");
        Assert.Equal(CrawlNoticeLevel.Warning, notice.Level);
        Assert.Contains(MOVED_TO, notice.Message);
    }

    /// <summary>
    /// The home page answers, then the site goes down: nothing answers, robots.txt included. On the fake clock, so the
    /// 1-minute pauses between tries and the 10 minutes before giving up pass in moments. Once the site is back, the
    /// crawl resumes where it stopped.
    /// </summary>
    [Fact]
    public async Task ASiteThatStaysDownStopsTheCrawlAndCanBeResumed()
    {
        var links = string.Concat(Enumerable.Range(0, 60).Select(page => $"<a href=\"/p{page}\">Page {page}</a> "));
        var isUp = true;
        var goesDownAfterTheHomePage = true;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (!Volatile.Read(ref isUp))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            switch (context.Request.Path.Value)
            {
                case "/":
                    Volatile.Write(ref isUp, !Volatile.Read(ref goesDownAfterTheHomePage));
                    return Html(context, links);
                case "/robots.txt":
                    return Status(context, 404);
                default:
                    return Html(context, "<p>Back again</p>");
            }
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        var path = NewDatabasePath();
        var options = Options() with { Concurrency = 2 };

        var summary = await CrawlOnFakeClockAsync(options, path, observer, time, LONG_STEP);

        // Ten minutes after it stopped answering, give or take a step of the clock.
        Assert.Equal(CrawlStatus.Stopped, summary.Status);
        Assert.Equal(1, observer.Count("site-down"));
        Assert.InRange(observer.FirstAt("site-down") - observer.FirstAt("site-unreachable"),
            RetryingFetcher.GIVE_UP_AFTER - LONG_STEP, RetryingFetcher.GIVE_UP_AFTER + TimeSpan.FromMinutes(2));

        // Nothing it couldn't reach was written off as broken: it's all still to do, for when the site is back. Only the
        // probe of the https:// version, which never answered, is recorded dead: probes are never held back.
        Assert.Equal(0L, Scalar(path, $"""
            SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id
            WHERE u.is_internal = 1 AND r.outcome IN ({(int)FetchOutcome.Timeout}, {(int)FetchOutcome.NetworkError})
              AND u.source <> {(int)DiscoverySource.SiteVersion}
            """));
        Assert.Equal(FetchOutcome.NetworkError, Outcome(path, $"https{_server.BaseUri.AbsoluteUri[4..]}"));
        Assert.Equal(60L, Scalar(path, "SELECT COUNT(*) FROM urls WHERE is_internal = 1 AND state IN (0, 1)"));

        Volatile.Write(ref goesDownAfterTheHomePage, false);
        Volatile.Write(ref isUp, true);
        await using var resumed = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path, Resume: true));
        var resumedSummary = await resumed.Completion.WaitAsync(CRAWL_TIMEOUT);
        Assert.Equal(CrawlStatus.Completed, resumedSummary.Status);
        Assert.Equal(60L, Scalar(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/p%' AND r.status_code = 200"));
        ProgressAgreement.AssertMatchesTheFile(resumedSummary.Progress, path);
    }

    /// <summary>
    /// Every other URL gets no response, never three in a row, so they are all held back for the end; then the site goes
    /// down for good. However many held-back URLs are left, the crawl gives up ten minutes after the site stopped
    /// answering.
    /// </summary>
    [Fact]
    public async Task ASiteThatGoesDownForTheLastTriesIsGivenUpOnInTime()
    {
        const int PAIRS = 40;
        var answered = 0;
        var links = string.Concat(Enumerable.Range(0, PAIRS).Select(page => $"<a href=\"/dead/{page}\">Dead</a> <a href=\"/ok/{page}\">OK</a> "));
        _server = await LoopbackServer.StartAsync(context =>
        {
            var requestPath = context.Request.Path.Value ?? "/";
            if (Volatile.Read(ref answered) == PAIRS || requestPath.StartsWith("/dead/", StringComparison.Ordinal))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            if (requestPath.StartsWith("/ok/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref answered);
            }

            return requestPath switch
            {
                "/robots.txt" => Status(context, 404),
                "/" => Html(context, links),
                _ => Html(context, "<p>OK</p>")
            };
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);

        var summary = await CrawlOnFakeClockAsync(Options() with { Concurrency = 1 }, NewDatabasePath(), observer, time, LONG_STEP);

        Assert.Equal(CrawlStatus.Stopped, summary.Status);
        Assert.InRange(observer.FirstAt("site-down") - observer.FirstAt("site-unreachable"),
            RetryingFetcher.GIVE_UP_AFTER - LONG_STEP, RetryingFetcher.GIVE_UP_AFTER + TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// The site links to a www twin that doesn't exist. Its URLs fail, then fail fast, while the site itself is crawled in
    /// full, robots.txt respected or not: it isn't paused or stopped as if it were down, and the twin's URLs aren't called
    /// blocked by a robots.txt nobody could read.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADeadWwwTwinDoesNotHoldUpTheSite(bool respectRobotsTxt)
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            var twin = $"http://www.127.0.0.1:{context.Connection.LocalPort}";
            return context.Request.Path.Value switch
            {
                "/" => Html(context, string.Concat(Enumerable.Range(0, 10).Select(page => $"<a href=\"/p{page}\">Page</a> "))
                                     + string.Concat(Enumerable.Range(0, 15).Select(page => $"<a href=\"{twin}/w{page}\">Twin</a> "))),
                "/robots.txt" => Status(context, 404),
                _ => Html(context, "<p>Page</p>")
            };
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        var path = NewDatabasePath();

        var summary = await CrawlOnFakeClockAsync(Options() with { RespectRobotsTxt = respectRobotsTxt }, path, observer, time);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(10L, Scalar(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'http://127.0.0.1:%/p%' AND r.status_code = 200"));
        Assert.Equal(15L, Scalar(path, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'http://www.%' AND r.outcome = {(int)FetchOutcome.NetworkError}"));
        Assert.DoesNotContain(observer.Notices, notice => notice.Code is "site-unreachable" or "site-down" or "robots-unreachable");
    }

    /// <summary>A section of the site that never answers comes before the pages that do; robots.txt answers throughout.</summary>
    [Fact]
    public async Task ASectionThatNeverAnswersDoesNotStopTheCrawl()
    {
        var links = string.Concat(Enumerable.Range(0, 30).Select(page => $"<a href=\"/dead/{page}\">Dead</a> ")
            .Concat(Enumerable.Range(0, 10).Select(page => $"<a href=\"/ok/{page}\">OK</a> ")));
        _server = await LoopbackServer.StartAsync(context =>
        {
            var requestPath = context.Request.Path.Value ?? "/";
            if (requestPath.StartsWith("/dead/", StringComparison.Ordinal))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return requestPath == "/robots.txt" ? Status(context, 404) : Html(context, requestPath == "/" ? links : "<p>OK</p>");
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        var path = NewDatabasePath();

        var summary = await CrawlOnFakeClockAsync(Options() with { Concurrency = 1 }, path, observer, time);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(10L, Scalar(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/ok/%' AND r.status_code = 200"));
        Assert.Equal(30L, Scalar(path, $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE '%/dead/%' AND r.outcome = {(int)FetchOutcome.NetworkError}"));
        Assert.Equal(0, observer.Count("site-unreachable"));
    }

    /// <summary>
    /// The site lives on https and goes down for three minutes once its home page has been read: from the first of its
    /// pages asked for. Started from http, which only redirects to https and keeps answering throughout, the crawl must
    /// wait https out just the same: https has answered, so it is down, not gone. (From http, the home page is asked for
    /// twice: by the probe of the https:// version, then in full where the start URL leads.)
    /// </summary>
    [Theory]
    [InlineData("https")]
    [InlineData("http")]
    public async Task ABriefOutageBehindARedirectingStartUrlIsWaitedOut(string startScheme)
    {
        var time = new FakeTimeProvider();
        long downSince = 0;
        var links = string.Concat(Enumerable.Range(0, 30).Select(page => $"<a href=\"/p{page}\">Page {page}</a> "));
        _server = await LoopbackServer.StartAsync(context =>
        {
            var requestPath = context.Request.Path.Value ?? "/";
            if (!context.Request.IsHttps)
            {
                context.Response.Headers.Location = $"https://127.0.0.1:{context.Connection.LocalPort}{requestPath}";
                return Status(context, 301);
            }

            if (requestPath.StartsWith("/p", StringComparison.Ordinal))
            {
                Interlocked.CompareExchange(ref downSince, time.GetUtcNow().UtcTicks, 0);
            }

            var since = Interlocked.Read(ref downSince);
            if (since != 0 && time.GetUtcNow().UtcTicks - since < TimeSpan.FromMinutes(3).Ticks)
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return requestPath switch
            {
                "/" => Html(context, links),
                "/robots.txt" => Status(context, 404),
                _ => Html(context, "<p>Page</p>")
            };
        }, https: true);
        var start = new UriBuilder(_server.BaseUri) { Scheme = startScheme }.Uri.AbsoluteUri;
        var options = Options() with { StartUrl = start, Concurrency = 2, TrustedCertificate = _server.Certificate };
        var observer = new RecordingObserver(time);
        var path = NewDatabasePath();

        var summary = await CrawlOnFakeClockAsync(options, path, observer, time);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(30L, Scalar(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'https://%/p%' AND r.status_code = 200"));
        Assert.Equal(0L, Scalar(path, $"SELECT COUNT(*) FROM responses WHERE error LIKE '{RetryingFetcher.NOT_CHECKED_ERROR}%'"));
        Assert.Equal(1, observer.Count("site-unreachable"));
    }

    /// <summary>
    /// The site goes down and is waited out; the crawl is paused for twelve minutes, during which nothing is asked of the
    /// site, and the site comes back. Resumed, the crawl carries on rather than give up on the time it spent paused.
    /// </summary>
    [Fact]
    public async Task TimeSpentPausedDoesNotCountTowardsGivingUp()
    {
        var links = string.Concat(Enumerable.Range(0, 20).Select(page => $"<a href=\"/p{page}\">Page {page}</a> "));
        var isUp = true;
        _server = await LoopbackServer.StartAsync(context =>
        {
            if (!Volatile.Read(ref isUp))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            switch (context.Request.Path.Value)
            {
                case "/":
                    Volatile.Write(ref isUp, false);
                    return Html(context, links);
                case "/robots.txt":
                    return Status(context, 404);
                default:
                    return Html(context, "<p>Back again</p>");
            }
        });
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        await using var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(Options() with { Concurrency = 2 }, NewDatabasePath()), observer);

        await FakeClock.PumpUntilAsync(time, () => observer.Count("site-unreachable") == 1);
        job.Pause();
        await FakeClock.PumpForAsync(time, TimeSpan.FromMinutes(12), LONG_STEP);
        Volatile.Write(ref isUp, true);
        job.Resume();
        var summary = await FakeClock.PumpAsync(time, job.Completion);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(0, observer.Count("site-down"));
    }

    /// <summary>
    /// The site links to a dead www twin. The twin's URLs that were asked for before it was found dead keep their real
    /// error, as another site's would, even after their one more try at the end meets the written-off twin. Continued, the
    /// crawl's counts come back as they were: the URLs never asked are not checked, not network errors.
    /// </summary>
    [Fact]
    public async Task UrlsOnADeadWwwTwinThatWereAskedForKeepTheirRealError()
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            var twin = $"http://www.127.0.0.1:{context.Connection.LocalPort}";
            return context.Request.Path.Value switch
            {
                "/" => Html(context, string.Concat(Enumerable.Range(0, 10).Select(page => $"<a href=\"{twin}/w{page}\">Twin</a> "))),
                "/robots.txt" => Status(context, 404),
                _ => Html(context, "<p>Page</p>")
            };
        });
        var time = new FakeTimeProvider();
        var path = NewDatabasePath();

        // Robots.txt ignored, so only the twin's URLs count towards its being found dead: the first three.
        var summary = await CrawlOnFakeClockAsync(Options() with { Concurrency = 1, RespectRobotsTxt = false }, path, new RecordingObserver(time), time);

        var twinErrors = TestCrawls.Rows(path, "SELECT u.url, r.outcome, r.error FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'http://www.%'");
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.True(twinErrors.Count(row => !row.Contains(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal)) == HostGate.UNREACHABLE_AFTER_URLS,
            string.Join(Environment.NewLine, twinErrors));
        Assert.Equal(7L, Scalar(path, $"SELECT COUNT(*) FROM responses WHERE error LIKE '{RetryingFetcher.NOT_CHECKED_ERROR}%'"));

        // The seven never asked are done with, so they're among the URLs fetched, but they weren't crawled: the URLs
        // crawled are the ones the URL grid has as fetched, in the crawl's last progress and in the library's summary.
        Assert.Equal(summary.Progress.Fetched - 7, summary.Progress.Crawled);
        var listed = CrawlSummaryReader.Read(path);
        Assert.Equal(summary.Progress.Fetched, listed.FetchedUrlCount);
        Assert.Equal(summary.Progress.Crawled, listed.CrawledUrlCount);
        Assert.Equal(listed.CrawledUrlCount, UrlGridReader.Query(path, new UrlQuery { Filters = [GridQueries.Boolean("fetched", true)], Limit = 1 }).Matching);
        Assert.Equal(7, UrlGridReader.Query(path, new UrlQuery { Filters = [GridQueries.Names("outcome", FilterOperator.In, "notChecked")], Limit = 1 }).Matching);

        await using var resumed = new CrawlEngine(NullLoggerFactory.Instance, time).Start(
            new CrawlRequest(Options() with { Concurrency = 1, RespectRobotsTxt = false }, path, Resume: true));
        var resumedSummary = await FakeClock.PumpAsync(time, resumed.Completion);
        Assert.Equal(CrawlStatus.Completed, resumedSummary.Status);
        Assert.Equal(7, resumedSummary.Progress.NoResponse.NotChecked);
        Assert.Equal(summary.Progress.Crawled, resumedSummary.Progress.Crawled);
        ProgressAgreement.AssertMatchesTheFile(resumedSummary.Progress, path);
    }

    /// <summary>
    /// The plain http side of an https site answers (its robots.txt does), but three of its URLs drop the connection.
    /// Those failed for reasons of their own: the rest of that origin is still fetched, and nothing is waited out.
    /// </summary>
    [Fact]
    public async Task ALiveOriginOtherThanTheStartsIsNotWrittenOffForAFewDeadUrls()
    {
        _server = await LoopbackServer.StartAsync(context =>
        {
            var requestPath = context.Request.Path.Value ?? "/";
            var plain = $"http://127.0.0.1:{context.Connection.LocalPort}";
            if (context.Request.IsHttps)
            {
                return requestPath switch
                {
                    "/" => Html(context, string.Concat(Enumerable.Range(0, 3).Select(page => $"<a href=\"{plain}/drop{page}\">Drop</a> "))
                                         + string.Concat(Enumerable.Range(0, 5).Select(page => $"<a href=\"{plain}/ok{page}\">OK</a> "))),
                    "/robots.txt" => Status(context, 404),
                    _ => Html(context, "<p>Page</p>")
                };
            }

            if (requestPath.StartsWith("/drop", StringComparison.Ordinal))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return requestPath == "/robots.txt" ? Status(context, 404) : Html(context, "<p>Plain page</p>");
        }, https: true);
        var time = new FakeTimeProvider();
        var observer = new RecordingObserver(time);
        var path = NewDatabasePath();
        var options = Options() with { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 1, TrustedCertificate = _server.Certificate };

        var summary = await CrawlOnFakeClockAsync(options, path, observer, time);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(5L, Scalar(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'http://%/ok%' AND r.status_code = 200"));
        Assert.Equal(0, observer.Count("site-unreachable"));
    }

    /// <summary>
    /// More URLs on a dead www twin than the crawler holds back, then, deeper, a page that drops its first request. The
    /// twin's URLs failed without a request aren't held back, so they leave room for that page's one more try.
    /// </summary>
    [Fact]
    public async Task UrlsOnAGoneOriginDoNotUseUpTheRetriesAtTheEnd()
    {
        // Two pages of twin links (a page's links are capped at 10,000), each ending with the next page, so the flaky page
        // is found, and asked for, after every twin URL.
        const int TWIN_LINKS_PER_PAGE = 5_025;
        var flakyRequests = 0;
        _server = await LoopbackServer.StartAsync(context =>
        {
            var twin = $"http://www.127.0.0.1:{context.Connection.LocalPort}";
            var requestPath = context.Request.Path.Value ?? "/";
            if (requestPath is "/hub0" or "/hub1")
            {
                var next = requestPath == "/hub0" ? "/hub1" : "/flaky";
                return Html(context, string.Concat(Enumerable.Range(0, TWIN_LINKS_PER_PAGE).Select(page => $"<a href=\"{twin}{requestPath}/w{page}\">T</a>"))
                                     + $"<a href=\"{next}\">Next</a>");
            }

            switch (requestPath)
            {
                case "/":
                    return Html(context, "<a href=\"/hub0\">Hub</a>");
                case "/flaky" when Interlocked.Increment(ref flakyRequests) == 1:
                    context.Abort();
                    return Task.CompletedTask;
                default:
                    return Html(context, "<p>Page</p>");
            }
        });
        var path = NewDatabasePath();

        var summary = await CrawlAsync(path, new RecordingObserver(), respectRobotsTxt: false);

        var flaky = $"{_server.BaseUri}flaky";
        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.True(Outcome(path, flaky) == FetchOutcome.Success, $"{flaky}: {Outcome(path, flaky)} ({Error(path, flaky)}) after {flakyRequests} requests; " +
            $"twin errors: {string.Join(", ", TestCrawls.Rows(path, "SELECT substr(error, 1, 40), COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url LIKE 'http://www.%' GROUP BY 1"))}");
        Assert.Equal(2, flakyRequests);
    }

    private CrawlOptions Options(int maxRetries = 0) =>
        new() { StartUrl = _server!.BaseUri.AbsoluteUri, MaxRetries = maxRetries, DiscoverSitemaps = false };

    private async Task<CrawlSummary> CrawlAsync(string path, ICrawlObserver observer, int maxRetries = 0, bool respectRobotsTxt = true)
    {
        var options = Options(maxRetries) with { RespectRobotsTxt = respectRobotsTxt };
        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path), observer);
        var summary = await job.Completion.WaitAsync(CRAWL_TIMEOUT);

        // Timeouts, network errors, URLs not checked and ones robots.txt refused are each counted where the file has them.
        ProgressAgreement.AssertMatchesTheFile(summary.Progress, path);
        return summary;
    }

    private static async Task<CrawlSummary> CrawlOnFakeClockAsync(CrawlOptions options, string path, ICrawlObserver observer, FakeTimeProvider time, TimeSpan? step = null)
    {
        await using var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, path), observer);
        var summary = await FakeClock.PumpAsync(time, job.Completion, step);
        ProgressAgreement.AssertMatchesTheFile(summary.Progress, path);
        return summary;
    }

    private string NewDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }

    private static FetchOutcome Outcome(string path, string url) =>
        (FetchOutcome)Convert.ToInt32(Scalar(path, $"SELECT r.outcome FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{url}'"),
            CultureInfo.InvariantCulture);

    private static string? Error(string path, string url) =>
        Scalar(path, $"SELECT r.error FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{url}'") as string;

    private static object? Scalar(string path, string sql)
    {
        using var connection = CrawlDatabase.OpenReader(path);
        return CrawlDatabase.Scalar(connection, sql);
    }

    private static Task Html(HttpContext context, string body)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><title>Test page</title></head><body>{body}</body></html>");
    }

    private static Task Text(HttpContext context, string body)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(body);
    }

    private static Task Status(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
