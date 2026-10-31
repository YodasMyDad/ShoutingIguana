using System.Collections.Concurrent;
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
/// Hosts that stop answering, on the fake clock. /drop closes the connection without a response; /ok answers. The
/// clock is pumped forward while a fetch is in flight, and the server notes the fake time of every request.
/// </summary>
public sealed class RetryingFetcherTests : IAsyncLifetime
{
    private static readonly TimeSpan PUMP_STEP = TimeSpan.FromMilliseconds(200);

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingObserver _observer = new();
    private readonly ConcurrentQueue<(int Port, DateTimeOffset At)> _requests = new();
    private LoopbackServer _server = null!;
    private HttpFetcher _http = null!;
    private RetryingFetcher _fetcher = null!;
    private HostGateRegistry _gates = null!;

    public async Task InitializeAsync()
    {
        _server = await LoopbackServer.StartAsync(RespondAsync);
        var options = new CrawlOptions { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 1 };
        var scope = new CrawlScope(_server.BaseUri, includeSubdomains: false, stayInStartFolder: false);
        _gates = new HostGateRegistry(scope, isInternal => new HostGate(isInternal ? 4 : 2, TimeSpan.Zero, _time));
        _http = new HttpFetcher(options, UserAgents.Resolve(options.UserAgent, null), scope, _time);
        _fetcher = new RetryingFetcher(_http, _gates, scope, options.MaxRetries, _observer, _time, new NoJitter());
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

        Assert.False(_gates.For(_server.OtherBaseUri).IsUnreachable);
        Assert.Equal(9, Requests(_server.OtherBaseUri));
    }

    [Fact]
    public async Task TheSiteItselfIsWaitedOutNotSkipped()
    {
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            await FetchAsync(_server.Url($"/drop?url={url}"));
        }

        Assert.Equal(1, _observer.Count("site-unreachable"));
        var tripped = Times(_server.BaseUri)[^1];

        // The next URL waits out the pause, and so does its retry after another unanswered attempt. Not before the
        // pause is over; the clock keeps moving while a request is in flight, hence the slack above it.
        var waited = await FetchAsync(_server.Url("/drop?url=3"));
        var times = Times(_server.BaseUri);
        var slack = TimeSpan.FromSeconds(10);
        Assert.DoesNotContain("Not checked", waited.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(8, times.Count);
        Assert.InRange(times[6] - tripped, RetryingFetcher.UNREACHABLE_PAUSE, RetryingFetcher.UNREACHABLE_PAUSE + slack);
        Assert.InRange(times[7] - times[6], RetryingFetcher.UNREACHABLE_PAUSE, RetryingFetcher.UNREACHABLE_PAUSE + slack);

        // An answer ends it.
        Assert.Equal(200, (await FetchAsync(_server.Url("/ok"))).StatusCode);
        Assert.False(_gates.For(_server.BaseUri).IsUnreachable);
        Assert.Equal(1, _observer.Count("site-unreachable"));
    }

    private async Task<FetchResponse> FetchAsync(Uri uri)
    {
        var fetch = _fetcher.FetchAsync(uri, FetchBody.None, CancellationToken.None);
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

    private Task RespondAsync(HttpContext context)
    {
        _requests.Enqueue((context.Connection.LocalPort, _time.GetUtcNow()));
        if (context.Request.Path == "/drop")
        {
            context.Abort();
            return Task.CompletedTask;
        }

        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync("ok");
    }
}
