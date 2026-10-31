using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.Engine.Urls;
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

        // The lock went with the writer, so another one can open the file at once, on every OS (and the crawl can be
        // resumed). Deleting also fails on Windows while any connection still has the file open.
        await using (CrawlWriter.Open(path, Options(), resume: true, new CrawlCounters(), NullLogger.Instance))
        {
        }

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AnAnalysisIsRefusedWhileACrawlIsWriting()
    {
        var path = _files.NewPath();
        await TestCrawls.RunAsync(Options() with { MaxUrls = 10 }, path);
        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);

        // A resume that has opened the file but not written anything yet: the window a status check alone left open.
        await using (CrawlWriter.Open(path, Options(), resume: true, new CrawlCounters(), NullLogger.Instance))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.AnalyzeAsync(path));
        }

        Assert.Equal(CrawlStatus.Running, CrawlStatsReader.Read(path).Status);
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'issues'"));
    }

    [Fact]
    public async Task ASecondWriterIsRefusedAtOnce()
    {
        var path = _files.NewPath();
        await TestCrawls.RunAsync(Options() with { MaxUrls = 10 }, path);

        // A crash leaves a crawl marked Running, so only a lock can tell a second resume that the first is still going.
        await using (CrawlWriter.Open(path, Options(), resume: true, new CrawlCounters(), NullLogger.Instance))
        {
            Assert.Throws<InvalidOperationException>(() => TestCrawls.Start(Options(), path, resume: true));
        }

        // While an analysis writes, a crawl can't start either, and is told so rather than failing after SQLite's timeouts.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Stopped}");
        }

        using (IssueWriter.Open(path, NullLogger.Instance))
        {
            var stopwatch = Stopwatch.StartNew();
            Assert.Throws<InvalidOperationException>(() => TestCrawls.Start(Options(), path, resume: true));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Refusing took {stopwatch.Elapsed}");
        }

        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path, resume: true)).Status);
    }

    [Fact]
    public async Task ACrashedCrawlStaysResumable()
    {
        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(Options() with { MaxRequestsPerSecond = 50 }, path))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) >= 10 FROM responses");
            await job.StopAsync();
        }

        // What a crash leaves behind: the crawl still marked Running and, on Linux and macOS, the lock file.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Running}");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path));
        await File.WriteAllTextAsync(path + "-lock", string.Empty);
        var summary = await TestCrawls.RunAsync(Options(), path, resume: true);

        Assert.Equal(CrawlStatus.Completed, summary.Status);
        Assert.Equal(_server.Site.GetExpectations().HtmlPages, CrawlStatsReader.Read(path).HtmlPageCount);
        Assert.False(File.Exists(path + "-lock"));
    }

    [Fact]
    public async Task StoresTheCanonicalThatIndexabilityGoesBy()
    {
        var path = _files.NewPath();
        var links = new OutLinkFactory(new CrawlScope(_server.BaseUri, includeSubdomains: false, stayInStartFolder: true));
        OutLink Link(string target, LinkKind kind, LinkFlags flags = LinkFlags.None) =>
            links.Create(new Uri(_server.BaseUri, target), kind, flags, LinkPosition.Head, null);

        await using (var writer = CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            await writer.DiscoverAsync([Link("/body-only", LinkKind.Hyperlink), Link("/body-and-header", LinkKind.Hyperlink)],
                DiscoverySource.Seed, null, CancellationToken.None);
            var claim = await writer.ClaimAsync(2, 0, CancellationToken.None);
            foreach (var item in claim.Internal)
            {
                // A canonical in the body comes first in document order, but Google ignores it; the header's counts.
                List<OutLink> canonicals = [Link("/from-body", LinkKind.Canonical, LinkFlags.OutsideHead)];
                if (item.Url.EndsWith("/body-and-header", StringComparison.Ordinal))
                {
                    canonicals.Add(Link("/from-header", LinkKind.Canonical, LinkFlags.FromHttpHeader));
                }

                var response = new ResponseRow { UrlId = item.Id, FetchedAt = 0, Outcome = FetchOutcome.Success, StatusCode = 200, IsHtml = true };
                await writer.RecordAsync(new PageRecord(item, response, canonicals, false, null, null), CancellationToken.None);
            }
        }

        var origin = _server.BaseUri.GetLeftPart(UriPartial.Authority);
        Assert.Equal(
            [$"{origin}/body-and-header | {origin}/from-header", $"{origin}/body-only | null"],
            TestCrawls.Rows(path, "SELECT u.url, c.url FROM responses r JOIN urls u ON u.id = r.url_id LEFT JOIN urls c ON c.id = r.canonical_url_id"));
    }

    [Fact]
    public async Task DownloadedBytesAreTheBodiesReadLiveAndAfterResuming()
    {
        var path = _files.NewPath();
        var links = new OutLinkFactory(new CrawlScope(_server.BaseUri, includeSubdomains: false, stayInStartFolder: true));
        OutLink Link(string target, LinkKind kind) => links.Create(new Uri(_server.BaseUri, target), kind, LinkFlags.None, LinkPosition.Content, null);
        var counters = new CrawlCounters();

        await using (var writer = CrawlWriter.Open(path, Options(), resume: false, counters, NullLogger.Instance))
        {
            await writer.DiscoverAsync([Link("/page", LinkKind.Hyperlink), Link("/photo.jpg", LinkKind.Image)], DiscoverySource.Seed, null, CancellationToken.None);
            var claim = await writer.ClaimAsync(2, 0, CancellationToken.None);
            Assert.Equal(2, claim.Internal.Count);
            foreach (var item in claim.Internal)
            {
                // A page read in full, 9,000 bytes sent as 2,000 compressed, and a photo only checked, which says it's 300 KB.
                var isPage = item.Url.EndsWith("/page", StringComparison.Ordinal);
                var response = new ResponseRow
                {
                    UrlId = item.Id, FetchedAt = 0, Outcome = FetchOutcome.Success, StatusCode = 200, IsHtml = isPage,
                    SizeBytes = isPage ? 2_000 : 300_000, BodyBytes = isPage ? 9_000 : null
                };
                await writer.RecordAsync(new PageRecord(item, response, [], false, null, null), CancellationToken.None);
            }
        }

        Assert.Equal(2_000, counters.Bytes);

        var resumed = new CrawlCounters();
        await using (CrawlWriter.Open(path, Options(), resume: true, resumed, NullLogger.Instance))
        {
            Assert.Equal(2_000, resumed.Bytes);
        }
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 4 };
}
