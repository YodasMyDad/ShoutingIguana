using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Urls;

public sealed class CrawlScopeTests
{
    [Fact]
    public void SameHostIsInternalAcrossSchemes()
    {
        var scope = new CrawlScope(new Uri("https://www.example.com/"), includeSubdomains: false, stayInStartFolder: true);

        Assert.True(scope.IsInternal(new Uri("https://www.example.com/page")));
        Assert.True(scope.IsInternal(new Uri("http://www.example.com/page")));
        Assert.False(scope.IsInternal(new Uri("https://shop.example.com/page")));
        Assert.False(scope.IsInternal(new Uri("https://other.com/")));
    }

    /// <summary>Starting from either is the same crawl, so a start URL that redirects to its twin crawls the site.</summary>
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://www.example.com/")]
    public void TheBareDomainAndItsWwwHostAreOneSite(string start)
    {
        var scope = new CrawlScope(new Uri(start), includeSubdomains: false, stayInStartFolder: false);

        Assert.True(scope.IsInternal(new Uri("https://example.com/page")));
        Assert.True(scope.IsInternal(new Uri("http://www.example.com/page")));
        Assert.False(scope.IsInternal(new Uri("https://shop.example.com/")));
        Assert.False(scope.IsInternal(new Uri("https://www.example.com.other.test/")));
        Assert.False(scope.IsInternal(new Uri("https://wwwexample.com/")));
    }

    [Fact]
    public void SubdomainsAreTheStartHostsOwn()
    {
        var scope = new CrawlScope(new Uri("https://shop.example.com/"), includeSubdomains: true, stayInStartFolder: false);

        Assert.True(scope.IsInternal(new Uri("https://eu.shop.example.com/")));
        Assert.True(scope.IsInternal(new Uri("https://www.shop.example.com/")));
        Assert.False(scope.IsInternal(new Uri("https://www.example.com/")));
    }

    [Fact]
    public void SubdomainsCountWhenIncluded()
    {
        var scope = new CrawlScope(new Uri("https://www.example.com/"), includeSubdomains: true, stayInStartFolder: true);

        Assert.True(scope.IsInternal(new Uri("https://example.com/")));
        Assert.True(scope.IsInternal(new Uri("https://blog.example.com/post")));
        Assert.False(scope.IsInternal(new Uri("https://notexample.com/")));
    }

    [Fact]
    public void DifferentPortIsADifferentSite()
    {
        var scope = new CrawlScope(new Uri("http://127.0.0.1:5000/"), includeSubdomains: false, stayInStartFolder: true);

        Assert.True(scope.IsInternal(new Uri("http://127.0.0.1:5000/a")));
        Assert.False(scope.IsInternal(new Uri("http://127.0.0.1:5001/a")));
    }

    [Fact]
    public void StartFolderLimitsCrawlingButNotScope()
    {
        var scope = new CrawlScope(new Uri("https://example.com/blog/post.html"), includeSubdomains: false, stayInStartFolder: true);

        Assert.True(scope.IsInStartFolder(new Uri("https://example.com/blog/another")));
        Assert.False(scope.IsInStartFolder(new Uri("https://example.com/shop")));
        Assert.True(scope.IsInternal(new Uri("https://example.com/shop")));
    }

    [Fact]
    public void WholeSiteWhenNotStayingInFolder()
    {
        var scope = new CrawlScope(new Uri("https://example.com/blog/"), includeSubdomains: false, stayInStartFolder: false);

        Assert.True(scope.IsInStartFolder(new Uri("https://example.com/shop")));
    }

    /// <summary>
    /// Progress and notices name a host of the site one way: as the start URL has it, for the start's host and its www twin,
    /// which are one site; a subdomain without its www.
    /// </summary>
    [Theory]
    [InlineData("https://www.example.com/", "example.com", "www.example.com")]
    [InlineData("https://www.example.com/", "www.example.com", "www.example.com")]
    [InlineData("https://example.com/", "www.example.com", "example.com")]
    [InlineData("https://Example.com/", "example.com", "example.com")]
    [InlineData("https://example.com/", "www.blog.example.com", "blog.example.com")]
    [InlineData("https://example.com/", "blog.example.com", "blog.example.com")]
    public void TheSitesHostsAreNamedAsTheStartUrlHasThem(string start, string host, string name)
    {
        var scope = new CrawlScope(new Uri(start), includeSubdomains: true, stayInStartFolder: false);

        Assert.Equal(name, scope.HostName(host));
    }
}
