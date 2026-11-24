using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>
/// A sidecar with crawls of the synthetic site in its library for the grids' endpoints, which only read them: one analysed
/// with its HTML stored, one cut short by a URL limit and analysed (so a check is skipped), and one never analysed. A test
/// that changes a crawl changes a <see cref="Copy"/>.
/// </summary>
public sealed class GridSidecar : IAsyncLifetime
{
    internal SyntheticSiteServer Site { get; private set; } = null!;

    internal TestSidecar Sidecar { get; private set; } = null!;

    /// <summary>The whole site, analysed, with its HTML.</summary>
    public string Path { get; private set; } = string.Empty;

    public string Id { get; private set; } = string.Empty;

    /// <summary>Ten URLs of the site, analysed: the checks that need the whole site were skipped.</summary>
    public string LimitedId { get; private set; } = string.Empty;

    /// <summary>Ten URLs of the site, never analysed.</summary>
    public string FreshId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Site = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        Sidecar = await TestSidecar.StartAsync();
        Path = await TestCrawlFiles.CrawlAsync(Site, System.IO.Path.Combine(Sidecar.DataDirectory, "site.sicrawl"), analyse: true,
            options: new CrawlOptions { StartUrl = Site.BaseUri.AbsoluteUri, StoreHtml = true, BrowserFallback = false });
        Id = CrawlIds.For(Path);
        LimitedId = CrawlIds.For(await TestCrawlFiles.CrawlAsync(Site, System.IO.Path.Combine(Sidecar.DataDirectory, "limited.sicrawl"), analyse: true));
        FreshId = CrawlIds.For(await TestCrawlFiles.CrawlAsync(Site, System.IO.Path.Combine(Sidecar.DataDirectory, "fresh.sicrawl")));
    }

    /// <summary>A copy of the analysed crawl in the library, under an id of its own.</summary>
    public string Copy(Action<string> change)
    {
        var copy = System.IO.Path.Combine(Sidecar.DataDirectory, $"copy-{Guid.NewGuid():N}.sicrawl");
        File.Copy(Path, copy);
        change(copy);
        return CrawlIds.For(copy);
    }

    /// <summary>A URL of the synthetic site, by its path.</summary>
    public string Url(string path) => Site.BaseUri.GetLeftPart(UriPartial.Authority) + path;

    public async Task DisposeAsync()
    {
        await Sidecar.DisposeAsync();
        await Site.DisposeAsync();
    }
}
