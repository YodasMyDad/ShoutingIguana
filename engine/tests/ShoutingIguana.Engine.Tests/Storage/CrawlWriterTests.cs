using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>The crawl writer: how it fails, and what it stores.</summary>
public sealed class CrawlWriterTests : IAsyncLifetime
{
    private readonly CrawlFiles _files = new();
    private SyntheticSiteServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 50 });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task AFailedWriteFailsTheCrawlWithItsOwnErrorAndClosesTheFile()
    {
        // A crawl file whose database rejects every response row, standing in for a full disk.
        var path = _files.NewPath();
        await using (CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection,
                "CREATE TRIGGER reject_responses BEFORE INSERT ON responses BEGIN SELECT RAISE(ABORT, 'injected write failure'); END;");
        }

        var summary = await TestCrawls.RunAsync(Options(), path, resume: true);

        Assert.Equal(CrawlStatus.Failed, summary.Status);
        Assert.Contains("injected write failure", summary.Error);
        Assert.Equal(CrawlStatus.Failed, CrawlStatsReader.Read(path).Status);

        // Deleting fails on Windows while any connection still has the file open.
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 4 };
}
