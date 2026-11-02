using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// One analysed crawl of the synthetic site, with its HTML stored, shared by the grid readers' tests, which only read it:
/// a test that changes a crawl changes a <see cref="Copy"/>. <see cref="Reference"/> holds every URL's grid values worked
/// out without the readers.
/// </summary>
public sealed class GridCrawl : IAsyncLifetime
{
    public const int PAGES = 240;

    private readonly CrawlFiles _files = new();

    public SyntheticSiteServer Server { get; private set; } = null!;

    public string Path { get; private set; } = string.Empty;

    internal GridReference Reference { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });
        Path = _files.NewPath();
        var crawl = await TestCrawls.RunAsync(new CrawlOptions { StartUrl = Server.BaseUri.AbsoluteUri, Concurrency = 8, StoreHtml = true }, Path);
        Assert.Equal(CrawlStatus.Completed, crawl.Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(Path);
        Reference = GridReference.Load(Path);
    }

    /// <summary>A copy of the crawl file, deleted with the fixture.</summary>
    public string Copy()
    {
        var copy = _files.NewPath();
        File.Copy(Path, copy);
        return copy;
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        _files.Dispose();
    }
}

[CollectionDefinition(NAME)]
public sealed class GridCrawlCollection : ICollectionFixture<GridCrawl>
{
    public const string NAME = "Grid crawl";
}
