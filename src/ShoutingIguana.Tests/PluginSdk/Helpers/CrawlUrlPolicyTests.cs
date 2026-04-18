using ShoutingIguana.PluginSdk.Helpers;
using Xunit;

namespace ShoutingIguana.Tests.PluginSdk.Helpers;

public class CrawlUrlPolicyTests
{
    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/path")]
    [InlineData("HTTPS://EXAMPLE.COM/")]
    public void AllowedSchemesPass(string url)
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(url, allowPrivateNetworks: false, out var reason);

        Assert.True(ok);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("file:///C:/windows/system32/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("ftp://example.com/")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void DisallowedSchemesAndGarbageRejected(string? url)
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(url, allowPrivateNetworks: false, out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.1.2.3/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://10.255.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.254/")]
    [InlineData("http://169.254.1.1/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://239.255.255.255/")]
    [InlineData("http://0.0.0.0/")]
    public void PrivateAndSpecialIPv4RangesRejectedByDefault(string url)
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(url, allowPrivateNetworks: false, out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[ff00::1]/")]
    [InlineData("http://[fc00::1]/")]
    public void PrivateAndSpecialIPv6RangesRejectedByDefault(string url)
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(url, allowPrivateNetworks: false, out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IPv4MappedIPv6PrivateAddressRejected()
    {
        // ::ffff:10.0.0.1 — the mapped form must still be recognised as RFC1918
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(
            "http://[::ffff:10.0.0.1]/",
            allowPrivateNetworks: false,
            out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://localhost/")]
    public void PrivateRangesPassWhenToggleEnabled(string url)
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(url, allowPrivateNetworks: true, out var reason);

        Assert.True(ok);
        Assert.Null(reason);
    }

    [Fact]
    public void NonHttpSchemeStillRejectedEvenWithPrivateNetworksAllowed()
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(
            "file:///C:/",
            allowPrivateNetworks: true,
            out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Fact]
    public void PublicIPv4Passes()
    {
        var ok = CrawlUrlPolicy.IsSafeCrawlTarget(
            "http://8.8.8.8/",
            allowPrivateNetworks: false,
            out var reason);

        Assert.True(ok);
        Assert.Null(reason);
    }
}
