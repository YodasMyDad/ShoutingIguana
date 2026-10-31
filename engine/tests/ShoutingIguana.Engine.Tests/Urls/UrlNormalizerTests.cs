using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Urls;

public sealed class UrlNormalizerTests
{
    [Theory]
    [InlineData("HTTP://Example.COM/Path?Q=1#frag", "http://example.com/Path?Q=1")]
    [InlineData("https://example.com:443/", "https://example.com/")]
    [InlineData("http://example.com:8080/a/", "http://example.com:8080/a/")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("http://example.com/a?", "http://example.com/a")]
    [InlineData("http://example.com/blog/", "http://example.com/blog/")]
    [InlineData("http://example.com/%7euser/%2fx", "http://example.com/~user/%2Fx")]
    [InlineData("http://bücher.de/", "http://xn--bcher-kva.de/")]
    public void Normalize_ProducesCanonicalForm(string input, string expected)
    {
        Assert.True(UrlNormalizer.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    [InlineData("")]
    public void TryNormalize_RejectsNonHttp(string input) =>
        Assert.False(UrlNormalizer.TryNormalize(input, out _));

    [Theory]
    [InlineData("../up", "https://example.com/up")]
    [InlineData("sibling.html", "https://example.com/dir/sibling.html")]
    [InlineData("/root", "https://example.com/root")]
    [InlineData("//cdn.example.com/x.js", "https://cdn.example.com/x.js")]
    [InlineData("  /pa\nth  ", "https://example.com/path")]
    [InlineData("?page=2", "https://example.com/dir/page.html?page=2")]
    public void TryResolve_ResolvesLikeABrowser(string href, string expected)
    {
        var page = new Uri("https://example.com/dir/page.html");

        Assert.True(UrlNormalizer.TryResolve(page, href, out var resolved));
        Assert.Equal(expected, UrlNormalizer.Normalize(resolved));
    }

    [Theory]
    [InlineData("javascript:void(0)")]
    [InlineData("tel:+441234567890")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("   ")]
    public void TryResolve_RejectsNonNavigableLinks(string href) =>
        Assert.False(UrlNormalizer.TryResolve(new Uri("https://example.com/"), href, out _));

    [Fact]
    public void GetFragment_ReturnsFragmentWithoutHash()
    {
        Assert.Equal("reviews", UrlNormalizer.GetFragment(new Uri("https://example.com/p#reviews")));
        Assert.Null(UrlNormalizer.GetFragment(new Uri("https://example.com/p")));
    }
}
