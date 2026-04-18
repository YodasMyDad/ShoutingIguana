using ShoutingIguana.PluginSdk.Helpers;
using Xunit;

namespace ShoutingIguana.Tests.PluginSdk.Helpers;

public class UrlHelperTests
{
    [Fact]
    public void NormalizeLowercasesSchemeAndHost()
    {
        var result = UrlHelper.Normalize("HTTPS://EXAMPLE.COM/Path");

        // Path case-preservation is not the concern here; scheme+host must be lowercase
        Assert.StartsWith("https://example.com/", result);
    }

    [Fact]
    public void NormalizeDropsFragment()
    {
        var result = UrlHelper.Normalize("https://example.com/page#section");

        Assert.DoesNotContain("#", result);
        Assert.DoesNotContain("section", result);
    }

    [Fact]
    public void NormalizePreservesTrailingSlash()
    {
        var withSlash = UrlHelper.Normalize("https://example.com/page/");
        var withoutSlash = UrlHelper.Normalize("https://example.com/page");

        Assert.EndsWith("/", withSlash);
        Assert.False(withoutSlash.EndsWith("/page/"));
        Assert.NotEqual(withSlash, withoutSlash);
    }

    [Fact]
    public void NormalizeEmptyReturnsEmpty()
    {
        Assert.Equal(string.Empty, UrlHelper.Normalize(string.Empty));
    }

    [Fact]
    public void ResolveAbsoluteUrlReturnsUnchanged()
    {
        var baseUri = new Uri("https://example.com/section/page.html");

        var result = UrlHelper.Resolve(baseUri, "https://other.com/path");

        Assert.Equal("https://other.com/path", result);
    }

    [Fact]
    public void ResolveProtocolRelativeUsesHttps()
    {
        var baseUri = new Uri("https://example.com/");

        var result = UrlHelper.Resolve(baseUri, "//cdn.example.com/asset.js");

        Assert.Equal("https://cdn.example.com/asset.js", result);
    }

    [Fact]
    public void ResolveRootRelativeAttachesToHost()
    {
        var baseUri = new Uri("https://example.com/section/page.html");

        var result = UrlHelper.Resolve(baseUri, "/about");

        Assert.Equal("https://example.com/about", result);
    }

    [Fact]
    public void ResolvePathRelativeResolvesAgainstDirectory()
    {
        var baseUri = new Uri("https://example.com/section/page.html");

        var result = UrlHelper.Resolve(baseUri, "other.html");

        Assert.Equal("https://example.com/section/other.html", result);
    }

    [Fact]
    public void ResolvePathRelativeHandlesParentDirectory()
    {
        var baseUri = new Uri("https://example.com/section/sub/page.html");

        var result = UrlHelper.Resolve(baseUri, "../other.html");

        Assert.Equal("https://example.com/section/other.html", result);
    }

    [Fact]
    public void ResolveWithBaseTagOverridesPageBase()
    {
        var pageUri = new Uri("https://example.com/section/page.html");
        var baseTagUri = new Uri("https://example.com/");

        var result = UrlHelper.Resolve(pageUri, "other.html", baseTagUri);

        Assert.Equal("https://example.com/other.html", result);
    }

    [Fact]
    public void ExtractBaseTagPicksFirstBaseHref()
    {
        var html = @"<html><head>
            <base href=""https://example.com/first/"">
            <base href=""https://example.com/second/"">
            </head></html>";
        var pageUri = new Uri("https://site.test/");

        var result = UrlHelper.ExtractBaseTag(html, pageUri);

        Assert.NotNull(result);
        Assert.Equal("https://example.com/first/", result!.ToString());
    }

    [Fact]
    public void ExtractBaseTagResolvesRelativeAgainstPage()
    {
        var html = @"<html><head><base href=""/root/""></head></html>";
        var pageUri = new Uri("https://example.com/section/page.html");

        var result = UrlHelper.ExtractBaseTag(html, pageUri);

        Assert.NotNull(result);
        Assert.Equal("https://example.com/root/", result!.ToString());
    }

    [Fact]
    public void ExtractBaseTagReturnsNullWhenMissing()
    {
        var html = "<html><head></head><body>hi</body></html>";
        var pageUri = new Uri("https://example.com/");

        var result = UrlHelper.ExtractBaseTag(html, pageUri);

        Assert.Null(result);
    }
}
