using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Urls;

/// <summary>The other versions of a site that the crawler probes and the analysis checks: one shared definition.</summary>
public sealed class SiteVersionsTests
{
    [Theory]
    [InlineData("https://example.com/", "https://www.example.com/", "http://example.com/", "http://www.example.com/")]
    [InlineData("https://www.example.com/", "https://example.com/", "http://example.com/", "http://www.example.com/")]
    [InlineData("http://example.com/", "https://example.com/", "https://www.example.com/", "http://www.example.com/")]
    [InlineData("http://www.example.com/", "https://example.com/", "https://www.example.com/", "http://example.com/")]
    public void EveryOtherSchemeAndHostOfTheSite(string start, string first, string second, string third) =>
        Assert.Equal([first, second, third], SiteVersions.Of(new Uri(start)));

    [Fact]
    public void ThePathAndQueryAreKept()
    {
        var versions = SiteVersions.Of(new Uri("https://example.com/shop/a%20b?page=2&sort=price"));

        Assert.Equal(
        [
            "https://www.example.com/shop/a%20b?page=2&sort=price", "http://example.com/shop/a%20b?page=2&sort=price",
            "http://www.example.com/shop/a%20b?page=2&sort=price"
        ], versions);
    }

    /// <summary>Only a leading www. goes, as CrawlScope has it: a subdomain's twin is its own www host, not the parent domain.</summary>
    [Fact]
    public void ASubdomainsTwinIsItsOwnWwwHost() =>
        Assert.Equal(["https://www.shop.example.com/", "http://shop.example.com/", "http://www.shop.example.com/"],
            SiteVersions.Of(new Uri("https://shop.example.com/")));

    /// <summary>On a port of its own the server answers both schemes there; on the default port each has its own.</summary>
    [Fact]
    public void ANonDefaultPortIsKeptForBothSchemes()
    {
        Assert.Equal(["https://www.example.com:8443/", "http://example.com:8443/", "http://www.example.com:8443/"],
            SiteVersions.Of(new Uri("https://example.com:8443/")));
        Assert.Equal(["https://example.com/", "https://www.example.com/", "http://www.example.com/"],
            SiteVersions.Of(new Uri("http://example.com:80/")));
    }

    /// <summary>An IP address or a single-label host has no www twin, only its other scheme.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:5000/", "https://127.0.0.1:5000/")]
    [InlineData("https://127.0.0.1/", "http://127.0.0.1/")]
    [InlineData("http://localhost:8080/a", "https://localhost:8080/a")]
    [InlineData("http://[::1]:8080/", "https://[::1]:8080/")]
    public void AHostWithoutAWwwTwinOnlySwapsTheScheme(string start, string expected) =>
        Assert.Equal([expected], SiteVersions.Of(new Uri(start)));

    [Theory]
    [InlineData("https://www.example.com/path", "https://www.example.com")]
    [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000")]
    public void AnOriginIsSchemeHostAndPort(string url, string origin) => Assert.Equal(origin, SiteVersions.OriginOf(url));
}
