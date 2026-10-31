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

    /// <summary>A registry like the crawl's: the site capped at one request a second, other sites uncapped.</summary>
    private static HostGateRegistry Registry(string startUrl, TimeProvider time) =>
        new(new CrawlScope(new Uri(startUrl), false, false),
            isInternal => new HostGate(4, isInternal ? TimeSpan.FromSeconds(1) : TimeSpan.Zero, time));
}
