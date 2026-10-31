using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Tests.Fetching;

namespace ShoutingIguana.Engine.Tests.Robots;

public sealed class RobotsCacheTests : IAsyncLifetime
{
    private readonly ConcurrentQueue<DateTimeOffset> _requests = new();
    private Func<HttpContext, int, Task> _robots = (context, _) => Respond(context, 404);
    private LoopbackServer _server = null!;
    private RobotsCacheHarness? _harness;

    public async Task InitializeAsync() => _server = await LoopbackServer.StartAsync(RespondAsync);

    public async Task DisposeAsync()
    {
        if (_harness is not null)
        {
            await _harness.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task NotFoundMeansNoRules()
    {
        var harness = Harness();

        var robots = await harness.GetAsync(_server.BaseUri);

        Assert.True(robots.IsAllowed("/anything"));
        Assert.Null(robots.UnreachableReason);
        Assert.Single(_requests);
        Assert.Empty(harness.Observer.Notices);
        Assert.Equal(404, Assert.Single(await harness.ReadRecordsAsync()).StatusCode);
    }

    [Theory]
    [InlineData(503, 3)]
    [InlineData(429, 3)]
    [InlineData(500, 1)]
    public async Task ServerErrorsAndRateLimitsAreRetriedThenDisallowEverything(int status, int requests)
    {
        _robots = (context, _) => Respond(context, status);
        var harness = Harness();

        var robots = await harness.GetAsync(_server.BaseUri);

        Assert.False(robots.IsAllowed("/"));
        Assert.Equal($"HTTP {status}", robots.UnreachableReason);
        Assert.Equal(requests, _requests.Count);
        Assert.Equal(1, harness.Observer.Count("robots-unreachable"));
        Assert.Equal(status, Assert.Single(await harness.ReadRecordsAsync()).StatusCode);
    }

    [Fact]
    public async Task RetryAfterIsHonouredAndARecoveryReadsTheRules()
    {
        _robots = (context, attempt) => attempt == 0
            ? Respond(context, 503, retryAfter: "30")
            : Respond(context, 200, "User-agent: *\nDisallow: /private/");
        var harness = Harness();

        var robots = await harness.GetAsync(_server.BaseUri);

        var times = _requests.ToArray();
        Assert.Equal(2, times.Length);
        Assert.InRange(times[1] - times[0], TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31));
        Assert.False(robots.IsAllowed("/private/page"));
        Assert.True(robots.IsAllowed("/public"));
        Assert.Empty(harness.Observer.Notices);
    }

    [Fact]
    public async Task AFailureIsOnlyKeptUntilTheCoolDownEnds()
    {
        _robots = (context, _) => Respond(context, 503);
        var harness = Harness(options => options with { MaxRetries = 0 });

        // Without retries nothing waits on the clock, so it stays where the failure happened.
        Assert.False((await harness.Robots.GetAsync(_server.BaseUri)).IsAllowed("/"));
        Assert.False((await harness.Robots.GetAsync(_server.BaseUri)).IsAllowed("/"));
        harness.Time.Advance(RobotsCache.RETRY_AFTER - TimeSpan.FromMilliseconds(1));
        await harness.Robots.GetAsync(_server.BaseUri);
        Assert.Single(_requests);

        harness.Time.Advance(TimeSpan.FromMilliseconds(1));
        _robots = (context, _) => Respond(context, 200, "User-agent: *\nDisallow: /private/");
        var recovered = await harness.Robots.GetAsync(_server.BaseUri);

        Assert.Equal(2, _requests.Count);
        Assert.True(recovered.IsAllowed("/"));
        Assert.Equal(1, harness.Observer.Count("robots-unreachable"));
    }

    [Fact]
    public async Task NoDisallowNoticeWhenRobotsTxtIsIgnored()
    {
        _robots = (context, _) => Respond(context, 503);
        var harness = Harness(options => options with { RespectRobotsTxt = false, MaxRetries = 0 });

        await harness.GetAsync(_server.BaseUri);

        Assert.Empty(harness.Observer.Notices);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public async Task FollowsUpToFiveRedirects(int hops, bool tooMany)
    {
        _robots = (context, attempt) => attempt < hops
            ? Redirect(context, $"/robots-{attempt + 1}.txt")
            : Respond(context, 200, "User-agent: *\nDisallow: /");
        var harness = Harness();

        var robots = await harness.GetAsync(_server.BaseUri);

        Assert.Equal(tooMany, robots.IsAllowed("/page"));
        var record = Assert.Single(await harness.ReadRecordsAsync());
        Assert.Equal(tooMany ? null : 200, record.StatusCode);
        Assert.Equal(tooMany ? "More than 5 redirects" : null, record.Error);
    }

    [Fact]
    public async Task ADeadExternalHostGetsNoVerdictSoItsLinksAreChecked()
    {
        var harness = Harness(options => options with { MaxRetries = 0 });
        var dead = RobotsCacheHarness.DeadSite();

        var robots = await harness.GetAsync(dead);

        Assert.True(robots.IsAllowed("/page"));
        Assert.Null(robots.UnreachableReason);
        Assert.Empty(harness.Observer.Notices);
        var record = Assert.Single(await harness.ReadRecordsAsync());
        Assert.Null(record.StatusCode);
        Assert.NotNull(record.Error);
    }

    [Fact]
    public async Task ADeadSiteOfOurOwnIsDisallowedAndReported()
    {
        var dead = RobotsCacheHarness.DeadSite();
        _harness = RobotsCacheHarness.Create(dead, options => options with { MaxRetries = 0 });

        var robots = await _harness.GetAsync(dead);

        Assert.False(robots.IsAllowed("/"));
        Assert.StartsWith("Could not connect", robots.UnreachableReason);
        Assert.Equal(1, _harness.Observer.Count("robots-unreachable"));
    }

    [Fact]
    public async Task CrawlDelayIsAppliedToTheSitesGateAndCapped()
    {
        _robots = (context, _) => Respond(context, 200, "User-agent: *\nCrawl-delay: 1e12");
        var harness = Harness(options => options with { RespectCrawlDelay = true });

        var robots = await harness.GetAsync(_server.BaseUri);

        Assert.Equal(RobotsTxtParser.MAX_CRAWL_DELAY_SECONDS, robots.CrawlDelaySeconds);
        var gate = harness.Gates.For(_server.BaseUri);
        (await gate.AcquireAsync(CancellationToken.None)).Dispose();
        var next = gate.AcquireAsync(CancellationToken.None).AsTask();
        harness.Time.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => next.WaitAsync(TimeSpan.FromMilliseconds(100)));
        harness.Time.Advance(TimeSpan.FromMilliseconds(1));
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task CrawlDelayIsIgnoredUnlessAskedFor()
    {
        _robots = (context, _) => Respond(context, 200, "User-agent: *\nCrawl-delay: 10");
        var harness = Harness();

        await harness.GetAsync(_server.BaseUri);

        var gate = harness.Gates.For(_server.BaseUri);
        (await gate.AcquireAsync(CancellationToken.None)).Dispose();
        Assert.True(gate.AcquireAsync(CancellationToken.None).AsTask().IsCompletedSuccessfully);
    }

    private RobotsCacheHarness Harness(Func<CrawlOptions, CrawlOptions>? configure = null) =>
        _harness = RobotsCacheHarness.Create(_server.BaseUri, configure);

    private Task RespondAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        if (!path.StartsWith("/robots", StringComparison.Ordinal))
        {
            return Respond(context, 404);
        }

        var attempt = _requests.Count;
        _requests.Enqueue(_harness?.Time.GetUtcNow() ?? DateTimeOffset.MinValue);
        return _robots(context, attempt);
    }

    private static Task Respond(HttpContext context, int status, string? body = null, string? retryAfter = null)
    {
        context.Response.StatusCode = status;
        if (retryAfter is not null)
        {
            context.Response.Headers.RetryAfter = retryAfter;
        }

        if (body is null)
        {
            return Task.CompletedTask;
        }

        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(body);
    }

    private static Task Redirect(HttpContext context, string location)
    {
        context.Response.StatusCode = 301;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }
}
