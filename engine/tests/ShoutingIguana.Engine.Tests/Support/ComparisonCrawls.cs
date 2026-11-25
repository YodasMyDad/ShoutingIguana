using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>
/// The synthetic site crawled at revision 0 and again, switched to revision 1, as its owner would publish fixes between
/// two crawls, each analysed: the comparison's tests only read them, and a test that changes one changes a
/// <see cref="Copy"/>. Over HTTPS too, on a smaller site, read on first use.
/// </summary>
public sealed class ComparisonCrawls : IAsyncLifetime
{
    /// <summary>The products 184 to 186 hold a duplicate-title cluster revision 1 renames at its first page.</summary>
    public const int PAGES = 1_000;

    /// <summary>Over half the plain site's, so the two read sizes alike and differ only in their origin.</summary>
    public const int HTTPS_PAGES = 600;

    private readonly CrawlFiles _files = new();
    private readonly SemaphoreSlim _httpsOnce = new(1, 1);
    private SyntheticSiteServer? _https;

    public SyntheticSiteServer Server { get; private set; } = null!;

    /// <summary>The crawl of revision 0.</summary>
    public string Then { get; private set; } = string.Empty;

    /// <summary>The crawl of revision 1, with the same options.</summary>
    public string Now { get; private set; } = string.Empty;

    public SyntheticSite Site => Server.Site;

    public SyntheticSite HttpsSite => _https!.Site;

    public Uri HttpsBaseUri => _https!.BaseUri;

    public string HttpsThen { get; private set; } = string.Empty;

    public string HttpsNow { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = PAGES });
        (Then, Now) = await CrawlBothAsync(Server, null);
    }

    /// <summary>The HTTPS site's two crawls, made the first time a test asks.</summary>
    public async Task<(string Then, string Now)> HttpsAsync()
    {
        await _httpsOnce.WaitAsync();
        try
        {
            if (_https is null)
            {
                _https = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = HTTPS_PAGES, Https = true });
                (HttpsThen, HttpsNow) = await CrawlBothAsync(_https, _https.Certificate);
            }

            return (HttpsThen, HttpsNow);
        }
        finally
        {
            _httpsOnce.Release();
        }
    }

    /// <summary>A copy of a crawl file, deleted with the fixture.</summary>
    public string Copy(string path)
    {
        var copy = _files.NewPath();
        File.Copy(path, copy);
        return copy;
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        if (_https is not null)
        {
            await _https.DisposeAsync();
        }

        _files.Dispose();
        _httpsOnce.Dispose();
    }

    private async Task<(string Then, string Now)> CrawlBothAsync(SyntheticSiteServer server, System.Security.Cryptography.X509Certificates.X509Certificate2? certificate)
    {
        var options = new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8, TrustedCertificate = certificate, BrowserFallback = false };
        var then = await CrawlAsync(options);
        server.Revision = 1;
        var now = await CrawlAsync(options);
        return (then, now);
    }

    private async Task<string> CrawlAsync(CrawlOptions options)
    {
        var path = _files.NewPath();
        var crawl = await TestCrawls.RunAsync(options, path);
        Assert.Equal(CrawlStatus.Completed, crawl.Status);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        return path;
    }
}

[CollectionDefinition(NAME)]
public sealed class ComparisonCrawlsCollection : ICollectionFixture<ComparisonCrawls>
{
    public const string NAME = "Comparison crawls";
}
