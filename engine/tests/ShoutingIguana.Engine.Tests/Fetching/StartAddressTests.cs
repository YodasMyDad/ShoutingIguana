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
