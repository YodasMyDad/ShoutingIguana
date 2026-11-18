using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Urls;

/// <summary>What a redirect changes (change C2 of docs/v2-new-checks.md): the redirect checks' rows and links-to-redirects' targets say it.</summary>
public sealed class RedirectKindsTests
{
    [Theory]
    [InlineData("http://example.com/a", "https://example.com/a", "httpToHttps")]
    [InlineData("https://example.com/a", "http://example.com/a", "httpsToHttp")]
    [InlineData("https://example.com/a", "https://www.example.com/a", "addsWww")]
    [InlineData("https://www.example.com/a", "https://example.com/a", "removesWww")]
    [InlineData("http://example.com/a", "https://www.example.com/a", "httpToHttps,addsWww")]
    [InlineData("https://example.com/a", "https://example.com/a/", "addsSlash")]
    [InlineData("https://example.com/a/", "https://example.com/a", "removesSlash")]
    [InlineData("https://example.com/Shop/Sofas", "https://example.com/shop/sofas", "changesCase")]
    [InlineData("http://example.com/A", "https://example.com/a/", "httpToHttps,addsSlash,changesCase")]
    [InlineData("https://example.com/a", "https://other.example/a", "otherHost")]
    [InlineData("http://example.com/a", "https://shop.example.com/b", "httpToHttps,otherHost")]
    [InlineData("https://example.com/old", "https://example.com/new/", "other")]
    [InlineData("https://example.com/a?x=1", "https://example.com/a", "other")]
    [InlineData("https://example.com:8443/a", "https://example.com/a", "otherHost")]
    public void ARedirectSaysWhatItChanges(string from, string to, string kind)
    {
        Assert.Equal(kind, RedirectKinds.Of(from, to));
    }

    [Fact]
    public void AUrlThatIsntOneHasNoKind()
    {
        Assert.Null(RedirectKinds.Of("https://example.com/a", "not a url"));
        Assert.Null(RedirectKinds.Of(null, "https://example.com/a"));
    }
}
