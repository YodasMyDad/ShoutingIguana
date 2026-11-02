using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class HostGateRegistryTests
{
    [Fact]
    public void TheSitesHttpAndHttpsUrlsShareOneGate()
    {
        var registry = Registry("https://example.com/", new FakeTimeProvider());

        Assert.Same(registry.For(new Uri("http://example.com/old-link")), registry.For(new Uri("https://example.com/new")));
        Assert.NotSame(registry.For(new Uri("https://example.com/")), registry.For(new Uri("https://partner.example/")));

        // Other sites keep a gate per host and port.
        Assert.NotSame(registry.For(new Uri("http://partner.example/")), registry.For(new Uri("https://partner.example/")));
    }

    [Fact]
    public void TheBareDomainAndItsWwwHostShareOneGate()
    {
        var registry = Registry("https://example.com/", new FakeTimeProvider());

        Assert.Same(registry.For(new Uri("https://example.com/")), registry.For(new Uri("https://www.example.com/")));
    }

    /// <summary>The gate shared by a site's bare domain and its www host is named as the start URL has it, whichever met it first.</summary>
    [Fact]
    public void TheSitesGatesAreNamedAsTheStartUrlHasItsHost()
    {
        var registry = Registry("https://www.example.com/", new FakeTimeProvider());
        registry.For(new Uri("http://example.com/old-link")).AddRobotsTxtWaiter(1);

        var (host, state) = Assert.Single(registry.ReadSiteStates());
        Assert.Equal("www.example.com", host);
        Assert.Equal(1, state.WaitingForRobotsTxt);
    }

    [Fact]
    public void AnotherPortOnTheSameHostIsAnotherSite()
    {
        List<bool> created = [];
        var registry = new HostGateRegistry(new CrawlScope(new Uri("http://127.0.0.1:5000/"), false, false), isInternal =>
        {
            created.Add(isInternal);
            return new HostGate(1, TimeSpan.Zero, new FakeTimeProvider());
        });

        Assert.NotSame(registry.For(new Uri("http://127.0.0.1:5000/")), registry.For(new Uri("http://127.0.0.1:5001/")));
        Assert.Equal([true, false], created);
    }

    [Fact]
    public async Task TheRateCapCoversHttpAndHttpsTogether()
    {
        var time = new FakeTimeProvider();
        var registry = Registry("https://example.com/", time);

        (await registry.For(new Uri("http://example.com/old-link")).AcquireAsync(CancellationToken.None)).Dispose();
        var https = registry.For(new Uri("https://example.com/new")).AcquireAsync(CancellationToken.None).AsTask();

        time.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => https.WaitAsync(TimeSpan.FromMilliseconds(100)));
        time.Advance(TimeSpan.FromMilliseconds(1));
        (await https.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public void AnAnswerFromTheSiteWritesOffADownOriginThatNeverAnswered()
    {
        var registry = Registry("https://example.com/", new FakeTimeProvider());
        var twinUri = new Uri("https://www.example.com/");
        var twin = registry.OriginOf(twinUri);
        var start = DateTimeOffset.UnixEpoch;
        registry.ReportDown(twin, twinUri, start);
        Assert.Equal(("https://www.example.com", TimeSpan.FromMinutes(1)), registry.LongestOutage(start + TimeSpan.FromMinutes(1)));

        registry.ReportAnswer(registry.OriginOf(new Uri("https://example.com/")), isInternal: true);

        Assert.True(twin.IsUnreachable);
        Assert.Null(registry.LongestOutage(start + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void AnOriginThatAnsweredBeforeStaysDownWhileAnotherAnswers()
    {
        var registry = Registry("http://example.com/", new FakeTimeProvider());
        var secureUri = new Uri("https://example.com/");
        var secure = registry.OriginOf(secureUri);
        secure.ReportAnswer();
        var start = DateTimeOffset.UnixEpoch;
        registry.ReportDown(secure, secureUri, start);

        // The http start URL only redirects to https, and keeps answering.
        registry.ReportAnswer(registry.OriginOf(new Uri("http://example.com/")), isInternal: true);

        Assert.False(secure.IsUnreachable);
        Assert.Equal(("https://example.com", TimeSpan.FromMinutes(4)), registry.LongestOutage(start + TimeSpan.FromMinutes(4)));
    }

    /// <summary>A registry like the crawl's: the site capped at one request a second, other sites uncapped.</summary>
    private static HostGateRegistry Registry(string startUrl, TimeProvider time) =>
        new(new CrawlScope(new Uri(startUrl), false, false),
            isInternal => new HostGate(4, isInternal ? TimeSpan.FromSeconds(1) : TimeSpan.Zero, time));
}
