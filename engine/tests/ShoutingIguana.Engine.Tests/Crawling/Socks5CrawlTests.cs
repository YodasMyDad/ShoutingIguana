using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A crawl through a SOCKS5 proxy that asks for a user name and password (RFC 1929), the proxy one of the test's own:
/// with the login in the proxy's address every request goes through it, and with a wrong one each fails saying so.
/// </summary>
public sealed class Socks5CrawlTests : IAsyncLifetime
{
    private const string USER = "crawler";
    private const string PASSWORD = "pa55:w@rd";

    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _site = null!;
    private Socks5ProxyServer _socks = null!;

    public async Task InitializeAsync()
    {
        _site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 12 });
        _socks = Socks5ProxyServer.Start(USER, PASSWORD);
    }

    public async Task DisposeAsync()
    {
        await _socks.DisposeAsync();
        await _site.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task ACrawlGoesThroughASocks5ProxyWithItsLogin()
    {
        var path = _files.NewPath();
        var crawl = await TestCrawls.RunAsync(Options(_socks.Address(USER, PASSWORD)), path);

        Assert.Equal(CrawlStatus.Completed, crawl.Status);
        var crawled = TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}");
        Assert.True(crawled > 5, $"{crawled} pages crawled");

        // It read the site as a crawl straight to it does: every URL, with the same outcome and status (the site's planted
        // failures among them).
        var direct = _files.NewPath();
        await TestCrawls.RunAsync(Options(_socks.Address(USER, PASSWORD)) with { ProxyUrl = null }, direct);
        const string ANSWERS = "SELECT u.url, r.outcome, r.status_code FROM responses r JOIN urls u ON u.id = r.url_id";
        Assert.Equal(TestCrawls.Rows(direct, ANSWERS), TestCrawls.Rows(path, ANSWERS));

        // Every connection the crawl made was the proxy's, each logged in with the password as typed (a colon and an @ in it).
        Assert.NotEmpty(_socks.Logins);
        Assert.All(_socks.Logins, login => Assert.Equal((USER, PASSWORD, true), login));
        // The site, and the partner site it links to, both on loopback.
        Assert.All(_socks.Tunnels, tunnel => Assert.StartsWith("127.0.0.1:", tunnel, StringComparison.Ordinal));
        Assert.Contains($"127.0.0.1:{_site.BaseUri.Port}", _socks.Tunnels);
        Assert.Equal(_socks.Greetings, _socks.Logins.Count);
    }

    /// <summary>
    /// The crawl can't read the start URL's robots.txt through the proxy, which it keeps asking for, as robots.txt it
    /// can't read refuses the site until it can (RFC 9309): the answer it keeps says why, in plain words.
    /// </summary>
    [Fact]
    public async Task AWrongLoginFailsSayingSo()
    {
        var path = await CrawlUntilRobotsTxtIsAnsweredAsync(_socks.Address(USER, "wrong"));

        Assert.Empty(_socks.Tunnels);
        Assert.Contains((USER, "wrong", false), _socks.Logins);
        Assert.DoesNotContain(_socks.Logins, login => login.Accepted);
        Assert.Equal(["null | The SOCKS5 proxy didn't accept the user name and password"], TestCrawls.Rows(path, $"SELECT status_code, error FROM robots WHERE origin = '{Origin}'"));
    }

    [Fact]
    public async Task NoLoginForAProxyThatAsksForOneSaysSo()
    {
        var path = await CrawlUntilRobotsTxtIsAnsweredAsync(_socks.Address());

        Assert.Empty(_socks.Logins);
        Assert.NotEqual(0, _socks.Greetings);
        Assert.Equal(["null | The SOCKS5 proxy asks for a user name and password"], TestCrawls.Rows(path, $"SELECT status_code, error FROM robots WHERE origin = '{Origin}'"));
    }

    private string Origin => _site.BaseUri.GetLeftPart(UriPartial.Authority);

    /// <summary>A crawl that can't get through goes on asking: it's stopped once the start URL's robots.txt has its answer.</summary>
    private async Task<string> CrawlUntilRobotsTxtIsAnsweredAsync(string proxyUrl)
    {
        var path = _files.NewPath();
        await using var job = TestCrawls.Start(Options(proxyUrl), path);
        await TestCrawls.WaitUntilAsync(path, $"SELECT COUNT(*) FROM robots WHERE origin = '{Origin}'");
        await job.StopAsync();
        return path;
    }

    private CrawlOptions Options(string proxyUrl) => new()
    {
        StartUrl = _site.BaseUri.AbsoluteUri, ProxyUrl = proxyUrl, MaxRetries = 0, DiscoverSitemaps = false, Concurrency = 4, BrowserFallback = false
    };
}
