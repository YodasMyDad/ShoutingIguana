using System.Net;
using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>Which addresses a crawl asks for, by where it started. Names are looked up by a fake resolver, never DNS.</summary>
public sealed class PrivateNetworkGuardTests
{
    private const string PUBLIC_START = "http://93.184.216.34/";

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.10.20.30", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("::", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:192.168.0.1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("2606:2800:220:1::1", false)]
    public void KnowsAPrivateAddress(string address, bool isPrivate) =>
        Assert.Equal(isPrivate, PrivateNetworkGuard.IsPrivate(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://[fd00::1]:8443/x")]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST.:3000/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://192.168.0.10/printer")]
    public async Task APublicSitesCrawlRefusesAPrivateAddressInTheUrl(string url) =>
        Assert.True(await new PrivateNetworkGuard(PUBLIC_START).RefusesAsync(new Uri(url)));

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://93.184.216.35/")]
    [InlineData("http://localhost.example.com/")]
    public async Task ItAsksForAPublicOne(string url) =>
        Assert.False(await new PrivateNetworkGuard(PUBLIC_START, Resolving("93.184.216.34")).RefusesAsync(new Uri(url)));

    [Theory]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://192.168.1.20/")]
    [InlineData("http://[::1]:5000/")]
    public async Task ACrawlThatStartedOnAPrivateAddressAsksForThemAll(string start)
    {
        var guard = new PrivateNetworkGuard(start, Resolving("10.0.0.5"));

        Assert.False(await guard.RefusesAsync(new Uri("http://10.0.0.1/")));
        Assert.False(await guard.RefusesAsync(new Uri("http://localhost:8080/")));
        Assert.Equal([IPAddress.Parse("10.0.0.5")], await guard.AddressesToConnectToAsync("intranet", CancellationToken.None));
    }

    /// <summary>An intranet site, started by its name: the name resolves to a private address, so the crawl is a private one.</summary>
    [Fact]
    public async Task AStartNameThatResolvesToAPrivateAddressMakesThePrivateOnesFair()
    {
        var guard = new PrivateNetworkGuard("http://intranet/", Resolving("10.20.30.40"));

        Assert.False(await guard.RefusesAsync(new Uri("http://192.168.0.1/")));
    }

    [Fact]
    public async Task AStartNameThatCantBeLookedUpCountsAsPublic()
    {
        var guard = new PrivateNetworkGuard("http://nowhere.example/", (_, _) => Task.FromException<IPAddress[]>(new System.Net.Sockets.SocketException(11001)));

        Assert.True(await guard.RefusesAsync(new Uri("http://127.0.0.1/")));
    }

    /// <summary>A public name that resolves to the user's own network (DNS rebinding, a stale record) isn't connected to.</summary>
    [Fact]
    public async Task APublicNameResolvingOnlyToPrivateAddressesIsRefusedAsItConnects()
    {
        var guard = new PrivateNetworkGuard(PUBLIC_START, Resolving("192.168.1.1", "::1"));

        var refused = await Assert.ThrowsAsync<PrivateNetworkAddressException>(() => guard.AddressesToConnectToAsync("rebound.example", CancellationToken.None));
        Assert.Contains("rebound.example", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMixedNameIsConnectedToOnlyOnItsPublicAddresses()
    {
        var guard = new PrivateNetworkGuard(PUBLIC_START, Resolving("10.0.0.1", "93.184.216.40"));

        Assert.Equal([IPAddress.Parse("93.184.216.40")], await guard.AddressesToConnectToAsync("mixed.example", CancellationToken.None));
    }

    private static Func<string, CancellationToken, Task<IPAddress[]>> Resolving(params string[] addresses) =>
        (_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());
}
