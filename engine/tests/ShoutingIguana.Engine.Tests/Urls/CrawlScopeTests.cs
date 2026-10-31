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
        Assert.False(scope.IsInternal(new Uri("https://example.com/page")));
        Assert.False(scope.IsInternal(new Uri("https://other.com/")));
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
}
