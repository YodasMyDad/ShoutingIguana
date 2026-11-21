using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>A crawl of the planted page stores the three values its rules name, and nothing else.</summary>
public sealed class CustomExtractionCrawlTests : IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 2 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        foreach (var path in _databases.SelectMany(path => new[] { path, path + "-wal", path + "-shm" }))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TheCrawlStoresThePlantedValuesAndNothingElse()
    {
        var path = NewDatabase();
        var start = new Uri(_server.BaseUri, SyntheticSite.CUSTOM_EXTRACT_PATH).AbsoluteUri;
        var options = new CrawlOptions
        {
            StartUrl = start,
            MaxDepth = 0,
            Concurrency = 2,
            DiscoverSitemaps = false,
            CheckResources = false,
            CheckExternalLinks = false,
            Rendering = JavaScriptRendering.Off,
            ExtractionRules =
            [
                new() { Name = "Price", SelectorType = ExtractionSelector.Css, Selector = ".plant-price" },
                new() { Name = "Sku", SelectorType = ExtractionSelector.XPath, Selector = "//span[@id='plant-sku']" },
                new() { Name = "Code", SelectorType = ExtractionSelector.Regex, Selector = @"extract-code:\s*(\S+)" },
                new() { Name = "Missing", SelectorType = ExtractionSelector.Css, Selector = ".not-on-the-page" }
            ]
        };

        await using var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, path));
        var summary = await job.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(CrawlStatus.Completed, summary.Status);

        var page = ExtractionReader.Read(path, new ExtractionQuery(null, null, 0, 50));

        Assert.Equal(4, page.Rules.Count);
        Assert.Equal(0, page.Rules.Single(rule => rule.Name == "Missing").Matches);
        Assert.Equal(3, page.Total);
        Assert.Equal(
            [
                ("Code", SyntheticSite.CUSTOM_EXTRACT_CODE, 1),
                ("Price", SyntheticSite.CUSTOM_EXTRACT_PRICE, 1),
                ("Sku", SyntheticSite.CUSTOM_EXTRACT_SKU, 1)
            ],
            page.Rows.Select(row => (row.Name, row.Value, row.Count)).OrderBy(row => row.Name).ToArray());
        Assert.All(page.Rows, row => Assert.EndsWith(SyntheticSite.CUSTOM_EXTRACT_PATH, row.Url, StringComparison.Ordinal));
    }

    private string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"si-extract-{Guid.NewGuid():N}.sicrawl");
        _databases.Add(path);
        return path;
    }
}
