using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Tests.Crawling;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Hosts that stop answering, on the fake clock. /drop closes the connection without a response, and so does everything
/// on the site while it is down; the rest answers. The clock is pumped forward while a fetch is in flight, and the
/// server notes the path and fake time of every request.
/// </summary>
public sealed class RetryingFetcherTests : IAsyncLifetime
{
    private static readonly TimeSpan PUMP_STEP = TimeSpan.FromMilliseconds(200);

    /// <summary>The clock keeps moving while a request is in flight, so a wait can come out this much longer.</summary>
    private static readonly TimeSpan SLACK = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingObserver _observer = new();
    private readonly ConcurrentQueue<(int Port, string Path, DateTimeOffset At)> _requests = new();
    private LoopbackServer _server = null!;
    private HttpFetcher _http = null!;
    private RetryingFetcher _fetcher = null!;
    private HostGateRegistry _gates = null!;
    private volatile bool _siteIsDown;

    public async Task InitializeAsync()
    {
        _server = await LoopbackServer.StartAsync(RespondAsync);
        (_http, _gates, _fetcher) = Create(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 1 });
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task AnotherSiteThatStopsAnsweringFailsFastAfterThreeUrls()
    {
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            var response = await FetchAsync(_server.OtherUrl($"/drop?url={url}"));
            Assert.Equal(FetchOutcome.NetworkError, response.Outcome);
            Assert.Null(response.StatusCode);
        }

        Assert.Equal(6, Requests(_server.OtherBaseUri));
        var skipped = await FetchAsync(_server.OtherUrl("/drop?url=3"));
        var working = await FetchAsync(_server.OtherUrl("/ok"));

        Assert.Equal(6, Requests(_server.OtherBaseUri));
        Assert.All([skipped, working], response =>
        {
            Assert.Equal(FetchOutcome.NetworkError, response.Outcome);
            Assert.StartsWith("Not checked:", response.Error);
        });
        Assert.Empty(_observer.Notices);
    }

    [Fact]
    public async Task AnyAnswerStartsTheCountAgain()
    {
        await FetchAsync(_server.OtherUrl("/drop?url=0"));
        await FetchAsync(_server.OtherUrl("/drop?url=1"));
        Assert.Equal(200, (await FetchAsync(_server.OtherUrl("/ok"))).StatusCode);
        await FetchAsync(_server.OtherUrl("/drop?url=2"));
        await FetchAsync(_server.OtherUrl("/drop?url=3"));

        Assert.False(_gates.OriginOf(_server.OtherBaseUri).IsUnreachable);
        Assert.Equal(9, Requests(_server.OtherBaseUri));
    }

    [Fact]
    public async Task TheSiteItselfIsWaitedOutNotSkipped()
    {
        _siteIsDown = true;
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            await FetchAsync(_server.Url($"/drop?url={url}"));
        }

        // Two attempts for each URL, then robots.txt is asked: it doesn't answer either, so the site is down.
        Assert.Equal(1, _observer.Count("site-unreachable"));
        Assert.Equal("/robots.txt", Paths(_server.BaseUri)[^1]);
        var tripped = Times(_server.BaseUri)[^1];

        // The next URL waits out the pause, and so does its retry after another unanswered attempt. Not before the
        // pause is over.
        var waited = await FetchAsync(_server.Url("/drop?url=3"));
        var times = Times(_server.BaseUri);
        Assert.DoesNotContain("Not checked", waited.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(9, times.Count);
        Assert.InRange(times[7] - tripped, RetryingFetcher.UNREACHABLE_PAUSE, RetryingFetcher.UNREACHABLE_PAUSE + SLACK);
        Assert.InRange(times[8] - times[7], RetryingFetcher.UNREACHABLE_PAUSE, RetryingFetcher.UNREACHABLE_PAUSE + SLACK);

        // An answer ends it, and the time the site was down with it: a site that was down for a while isn't given up
        // on later.
        _siteIsDown = false;
        Assert.Equal(200, (await FetchAsync(_server.Url("/ok"))).StatusCode);
        Assert.False(_gates.OriginOf(_server.BaseUri).IsDown);
        Assert.Null(_gates.LongestOutage(_time.GetUtcNow()));
        Assert.Equal(1, _observer.Count("site-unreachable"));
    }

    [Fact]
    public async Task UrlsThatGetNoResponseWhileRobotsTxtAnswersAreNotWaitedOut()
    {
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            await FetchAsync(_server.Url($"/drop?url={url}"));
        }

        Assert.Equal("/robots.txt", Paths(_server.BaseUri)[^1]);
        var asked = Times(_server.BaseUri)[^1];

        // robots.txt answered, so the site is up and these URLs failed for reasons of their own.
        var next = await FetchAsync(_server.Url("/drop?url=3"));

        Assert.DoesNotContain("Not checked", next.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.InRange(Times(_server.BaseUri)[7] - asked, TimeSpan.Zero, SLACK);
        Assert.False(_gates.OriginOf(_server.BaseUri).IsDown);
        Assert.Empty(_observer.Notices);
    }

    /// <summary>A www twin whose name doesn't resolve, or https on a port that only speaks http, while the site answers.</summary>
    [Theory]
    [InlineData("http://www.127.0.0.1:{0}/")]
    [InlineData("https://127.0.0.1:{0}/")]
    public async Task AnOriginOfTheSiteThatStopsAnsweringFailsFastWhileTheSiteAnswers(string deadOrigin)
    {
        var dead = new Uri(string.Format(CultureInfo.InvariantCulture, deadOrigin, _server.BaseUri.Port));
        Assert.Same(_gates.For(_server.BaseUri), _gates.For(dead));

        await AssertFailsFastWhileTheSiteAnswersAsync(dead, _fetcher, _gates);
    }

    [Fact]
    public async Task AnIncludedSubdomainThatStopsAnsweringFailsFastLikeAnotherSite()
    {
        // Names under 127.0.0.1 never resolve, so a subdomain there gets no answer at all, like a dead CDN.
        var (http, gates, fetcher) = Create(new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, IncludeSubdomains = true, MaxRetries = 0 });
        using var disposeHttp = http;
        var cdn = new Uri($"http://cdn.127.0.0.1:{_server.BaseUri.Port}/");
        Assert.True(new CrawlScope(_server.BaseUri, includeSubdomains: true, stayInStartFolder: false).IsInternal(cdn));

        await AssertFailsFastWhileTheSiteAnswersAsync(cdn, fetcher, gates);
    }

    /// <summary>
    /// Nothing on the site answers, so its https, which never has, is waited out. Then its plain http answers: the site is
    /// up after all, and https, still never having answered, is gone like a closed port.
    /// </summary>
    [Fact]
    public async Task AnOriginThatNeverAnsweredIsWrittenOffOnceTheSiteAnswers()
    {
        var httpIsDown = true;
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.IsHttps || Volatile.Read(ref httpIsDown))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("ok");
        }, https: true);
        var (http, gates, fetcher) = Create(new CrawlOptions
        {
            StartUrl = server.BaseUri.AbsoluteUri,
            MaxRetries = 0,
            TrustedCertificate = server.Certificate
        });
        using var disposeHttp = http;
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            await FetchAsync(server.Url($"/page-{url}"), fetcher);
        }

        var secure = gates.OriginOf(server.BaseUri);
        Assert.True(secure.IsDown);

        Volatile.Write(ref httpIsDown, false);
        var plain = new UriBuilder(server.Url("/old-link")) { Scheme = Uri.UriSchemeHttp }.Uri;
        Assert.Equal(200, (await FetchAsync(plain, fetcher)).StatusCode);

        Assert.False(secure.IsDown);
        Assert.True(secure.IsUnreachable);
        Assert.Null(gates.LongestOutage(_time.GetUtcNow()));
        Assert.Equal(1, _observer.Count("site-unreachable"));
    }

    /// <summary>
    /// The start URL is http, which only redirects to https, where the site is. https answers, then stops for a while,
    /// while the http redirects keep coming: https has answered, so it is waited out, never written off.
    /// </summary>
    [Fact]
    public async Task AnOriginThatAnsweredBeforeIsWaitedOutWhileAnotherKeepsAnswering()
    {
        var httpsIsDown = false;
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            if (!context.Request.IsHttps)
            {
                context.Response.StatusCode = 301;
                context.Response.Headers.Location = $"https://127.0.0.1:{context.Connection.LocalPort}{context.Request.Path}";
                return Task.CompletedTask;
            }

            if (Volatile.Read(ref httpsIsDown))
            {
                context.Abort();
                return Task.CompletedTask;
            }

            return context.Response.WriteAsync("ok");
        }, https: true);
        var start = new UriBuilder(server.BaseUri) { Scheme = Uri.UriSchemeHttp }.Uri;
        var (http, gates, fetcher) = Create(new CrawlOptions { StartUrl = start.AbsoluteUri, MaxRetries = 0, TrustedCertificate = server.Certificate });
        using var disposeHttp = http;
        Assert.Equal(301, (await FetchAsync(start, fetcher)).StatusCode);
        Assert.Equal(200, (await FetchAsync(server.BaseUri, fetcher)).StatusCode);

        // The redirect answered last when https stops, so it is what stands for the site.
        Assert.Equal(301, (await FetchAsync(new Uri(start, "/old-link"), fetcher)).StatusCode);
        Volatile.Write(ref httpsIsDown, true);
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            await FetchAsync(server.Url($"/page-{url}"), fetcher);
        }

        var waited = await FetchAsync(server.Url("/page-3"), fetcher);

        Assert.DoesNotContain("Not checked", waited.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.True(gates.OriginOf(server.BaseUri).IsDown);
        Assert.Equal(1, _observer.Count("site-unreachable"));
    }

    [Fact]
    public async Task ARetryAfterIsWaitedForAtMostAMinute()
    {
        var response = await FetchAsync(_server.Url("/bad-gateway"));

        var times = Times(_server.BaseUri);
        Assert.Equal(502, response.StatusCode);
        Assert.Equal(2, times.Count);
        Assert.InRange(times[1] - times[0], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1) + SLACK);
    }

    /// <summary>
    /// Three URLs on <paramref name="dead"/>, which has never answered, get no response. Its robots.txt doesn't answer
    /// either, but the site's does: only that origin is gone, so its next URL fails without a request, and the site isn't
    /// paused.
    /// </summary>
    private async Task AssertFailsFastWhileTheSiteAnswersAsync(Uri dead, RetryingFetcher fetcher, HostGateRegistry gates)
    {
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            Assert.Null((await FetchAsync(new Uri(dead, $"{url}.jpg"), fetcher)).StatusCode);
        }

        var skipped = await FetchAsync(new Uri(dead, "3.jpg"), fetcher);
        var asked = Times(_server.BaseUri)[^1];
        var site = await FetchAsync(_server.Url("/ok"), fetcher);

        Assert.StartsWith("Not checked:", skipped.Error);
        Assert.Equal(["/robots.txt", "/ok"], Paths(_server.BaseUri));
        Assert.Equal(200, site.StatusCode);
        Assert.InRange(Times(_server.BaseUri)[^1] - asked, TimeSpan.Zero, SLACK);
        Assert.Null(gates.LongestOutage(_time.GetUtcNow()));
        Assert.Empty(_observer.Notices);
    }

    private (HttpFetcher Http, HostGateRegistry Gates, RetryingFetcher Fetcher) Create(CrawlOptions options)
    {
        var scope = new CrawlScope(new Uri(options.StartUrl), options.IncludeSubdomains, stayInStartFolder: false);
        var gates = new HostGateRegistry(scope, isInternal => new HostGate(isInternal ? 4 : 2, TimeSpan.Zero, _time));
        var http = new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null), scope, _time);
        return (http, gates, new RetryingFetcher(http, gates, scope, options.MaxRetries, _observer, _time, new NoJitter()));
    }

    private async Task<FetchResponse> FetchAsync(Uri uri, RetryingFetcher? fetcher = null)
    {
        var fetch = (fetcher ?? _fetcher).FetchAsync(uri, FetchBody.None, CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!fetch.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Fetching {uri} did not finish.");
            }

            _time.Advance(PUMP_STEP);
            await Task.Delay(5);
        }

        return await fetch;
    }

    private int Requests(Uri site) => _requests.Count(request => request.Port == site.Port);

    private List<DateTimeOffset> Times(Uri site) => [.. _requests.Where(request => request.Port == site.Port).Select(request => request.At)];

    private List<string> Paths(Uri site) => [.. _requests.Where(request => request.Port == site.Port).Select(request => request.Path)];

    private Task RespondAsync(HttpContext context)
    {
        var port = context.Connection.LocalPort;
        _requests.Enqueue((port, context.Request.Path.Value ?? "/", _time.GetUtcNow()));
        if (context.Request.Path == "/drop" || (_siteIsDown && port == _server.BaseUri.Port))
        {
            context.Abort();
            return Task.CompletedTask;
        }

        if (context.Request.Path == "/bad-gateway")
        {
            context.Response.StatusCode = 502;
            context.Response.Headers.RetryAfter = "3600";
            return Task.CompletedTask;
        }

        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync("ok");
    }
}
