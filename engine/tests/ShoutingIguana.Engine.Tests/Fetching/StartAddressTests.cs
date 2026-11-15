using Microsoft.AspNetCore.Http;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Tests.Robots;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// An address typed without a scheme starts where the site answers. The closing audit's case: a site that only serves
/// http, typed as "127.0.0.1:port", was crawled on https, read nothing and blamed robots.txt.
/// </summary>
public sealed class StartAddressTests
{
    private static readonly CrawlOptions OPTIONS = new() { StartUrl = "https://example.com/" };

    [Fact]
    public async Task ASiteThatOnlyServesHttpStartsOnHttp()
    {
        await using var server = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("<p>Home</p>"));
        var address = server.BaseUri.Authority;

        var choice = await StartAddress.ChooseAsync(address, OPTIONS, CancellationToken.None);

        Assert.Equal(new StartAddressChoice($"http://{address}", StartAddressReason.Http), choice);
    }

    [Fact]
    public async Task ASiteOnHttpsStartsOnHttpsEvenWhenItAnswersAnError()
    {
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = context.Request.IsHttps ? 503 : 200;
            return Task.CompletedTask;
        }, https: true);
        var address = server.BaseUri.Authority + "/shop";

        var choice = await StartAddress.ChooseAsync(address, OPTIONS with { TrustedCertificate = server.Certificate }, CancellationToken.None);

        Assert.Equal(new StartAddressChoice($"https://{address}", StartAddressReason.Https), choice);
    }

    [Fact]
    public async Task AnAddressThatAnswersOnNeitherSchemeKeepsHttps()
    {
        var address = RobotsCacheHarness.DeadSite().Authority;

        var choice = await StartAddress.ChooseAsync(address, OPTIONS, CancellationToken.None);

        Assert.Equal(new StartAddressChoice($"https://{address}", StartAddressReason.NoAnswer), choice);
    }

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("  HTTPS://example.invalid/page  ")]
    public async Task AnAddressWithItsSchemeIsUsedAsTypedWithoutAsking(string address)
    {
        var requests = 0;
        await using var server = await LoopbackServer.StartAsync(_ =>
        {
            Interlocked.Increment(ref requests);
            return Task.CompletedTask;
        });

        var choice = await StartAddress.ChooseAsync(address, OPTIONS, CancellationToken.None);

        Assert.Equal(new StartAddressChoice(address.Trim(), StartAddressReason.Typed), choice);
        Assert.Equal(0, requests);
    }

    /// <summary>
    /// Round 5's review (R2-1): through a proxy, the proxy's own answer was taken for the site's, so an address that
    /// doesn't exist started on http. A 407 is the proxy asking for its password (the saved defaults mask it), and a
    /// 5xx over plain http is the proxy saying it couldn't reach the site; a refused tunnel is no answer either.
    /// </summary>
    [Theory]
    [InlineData(407)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task AProxysOwnAnswerIsNotTheSiteAnswering(int proxyStatus)
    {
        var asked = new List<string>();
        await using var proxy = await LoopbackServer.StartAsync(context =>
        {
            lock (asked)
            {
                asked.Add(context.Request.Method);
            }

            context.Response.StatusCode = proxyStatus;
            return Task.CompletedTask;
        });
        var options = new CrawlOptions
        {
            StartUrl = "https://example.com/",
            ProxyUrl = new UriBuilder(proxy.BaseUri) { UserName = "user", Password = "secret" }.Uri.AbsoluteUri
        }.WithoutSecrets();

        var choice = await StartAddress.ChooseAsync("no-such-site.invalid", options, CancellationToken.None);

        Assert.Equal(new StartAddressChoice("https://no-such-site.invalid", StartAddressReason.NoAnswer), choice);
        Assert.Equal(["CONNECT", "GET"], asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASiteThatOnlyServesHttpThroughAProxyStillStartsOnHttp()
    {
        await using var proxy = await LoopbackServer.StartAsync(context =>
        {
            // Refuses the https tunnel, and gives the site's page over plain http.
            context.Response.StatusCode = HttpMethods.IsConnect(context.Request.Method) ? 502 : 200;
            return Task.CompletedTask;
        });

        var choice = await StartAddress.ChooseAsync("http-only.invalid", OPTIONS with { ProxyUrl = proxy.BaseUri.AbsoluteUri }, CancellationToken.None);

        Assert.Equal(new StartAddressChoice("http://http-only.invalid", StartAddressReason.Http), choice);
    }

    [Fact]
    public async Task ASiteAnsweringAnErrorWithoutAProxyIsStillThere()
    {
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            context.Response.StatusCode = 503;
            return Task.CompletedTask;
        });

        var choice = await StartAddress.ChooseAsync(server.BaseUri.Authority, OPTIONS, CancellationToken.None);

        Assert.Equal(StartAddressReason.Http, choice.Reason);
    }

    [Fact]
    public async Task ExtraHeadersAreNeverSentOnAGuess()
    {
        string? secret = null;
        await using var server = await LoopbackServer.StartAsync(context =>
        {
            secret ??= context.Request.Headers["X-Allowlist-Token"];
            return Task.CompletedTask;
        });
        var options = OPTIONS with { ExtraHeaders = new Dictionary<string, string> { ["X-Allowlist-Token"] = "s3cret" } };

        await StartAddress.ChooseAsync(server.BaseUri.Authority, options, CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(secret));
    }
}
