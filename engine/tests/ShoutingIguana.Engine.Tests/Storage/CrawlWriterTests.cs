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
using ShoutingIguana.Engine.Tests.Crawling;
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

        // The injected trigger goes before a reader confirms the stored status: readers refuse a file with any trigger.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, "DROP TRIGGER reject_responses");
        }

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
    public async Task AWriteThatFailsAsTheCrawlEndsFailsTheCrawl()
    {
        // A crawl file whose database rejects only the last write, the one that marks the crawl completed.
        var path = _files.NewPath();
        await using (CrawlWriter.Open(path, Options(), resume: false, new CrawlCounters(), NullLogger.Instance))
        {
        }

        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"""
                CREATE TRIGGER reject_completion BEFORE UPDATE OF status ON crawl WHEN NEW.status = {(int)CrawlStatus.Completed}
                BEGIN SELECT RAISE(ABORT, 'injected write failure'); END;
                """);
        }

        var summary = await TestCrawls.RunAsync(Options(), path, resume: true);

        Assert.Equal(CrawlStatus.Failed, summary.Status);
        Assert.Contains("injected write failure", summary.Error);

        // The injected trigger goes before a reader confirms the stored status: readers refuse a file with any trigger.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, "DROP TRIGGER reject_completion");
        }

        Assert.Equal(CrawlStatus.Failed, CrawlStatsReader.Read(path).Status);
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
            await Assert.ThrowsAsync<CrawlFileInUseException>(() => analyzer.AnalyzeAsync(path));
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
            Assert.Throws<CrawlFileInUseException>(() => TestCrawls.Start(Options(), path, resume: true));
        }

        // While an analysis writes, a crawl can't start either, and is told so rather than failing after SQLite's timeouts.
        using (var connection = CrawlDatabase.OpenWriter(path))
        {
            CrawlDatabase.Execute(connection, $"UPDATE crawl SET status = {(int)CrawlStatus.Stopped}");
        }

        using (IssueWriter.Open(path, NullLogger.Instance))
        {
            var stopwatch = Stopwatch.StartNew();
            Assert.Throws<CrawlFileInUseException>(() => TestCrawls.Start(Options(), path, resume: true));
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

        // The lock file goes with the lock on Windows; elsewhere it stays for the next writer to lock (see CrawlFileLock).
        Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(path + "-lock"));
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

        Assert.Equal(2_000, counters.Current.Bytes);

        var resumed = new CrawlCounters();
        await using (CrawlWriter.Open(path, Options(), resume: true, resumed, NullLogger.Instance))
        {
            Assert.Equal(2_000, resumed.Current.Bytes);
        }
    }

    /// <summary>
    /// A site-version probe's redirect changes nothing the site itself led to: a page the site only status-checks (an
    /// iframe) isn't promoted to a full crawl, and another probe stays a probe. A URL only a probe leads to is
    /// status-checked as a probe too, never crawled, so a version that answers can't get the site crawled twice.
    /// </summary>
    [Fact]
    public async Task AProbesRedirectChangesNothingTheSiteLedTo()
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/shop/");
        var (options, link) = Site(start);

        await using (var writer = CrawlWriter.Open(path, options, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            await SeedAsync(writer, link, start, SiteVersions.Of(start));
            var claim = await writer.ClaimAsync(10, 10, CancellationToken.None);
            FrontierItem Item(string url) => claim.Internal.Single(item => item.Url == url);

            await RecordAsync(writer, Item("https://example.com/shop/"), FetchOutcome.Success, link("https://example.com/shop/embed", LinkKind.Iframe));
            await RecordAsync(writer, Item("https://www.example.com/shop/"), FetchOutcome.Redirect, link("https://example.com/shop/embed", LinkKind.Redirect));
            await RecordAsync(writer, Item("http://example.com/shop/"), FetchOutcome.Redirect, link("https://www.example.com/shop/", LinkKind.Redirect));
            await RecordAsync(writer, Item("http://www.example.com/shop/"), FetchOutcome.Redirect, link("http://www.example.com/landing", LinkKind.Redirect));
        }

        Assert.Equal(
        [
            Row("http://example.com/shop/", FetchMode.Check, 0, UrlState.Done, SkipReason.None, DiscoverySource.SiteVersion),
            Row("http://www.example.com/landing", FetchMode.Check, 0, UrlState.Queued, SkipReason.None, DiscoverySource.SiteVersion),
            Row("http://www.example.com/shop/", FetchMode.Check, 0, UrlState.Done, SkipReason.None, DiscoverySource.SiteVersion),
            Row("https://example.com/shop/", FetchMode.Crawl, 0, UrlState.Done, SkipReason.None, DiscoverySource.Seed),
            Row("https://example.com/shop/embed", FetchMode.Check, 1, UrlState.Queued, SkipReason.None, DiscoverySource.Link),
            Row("https://www.example.com/shop/", FetchMode.Check, 0, UrlState.Done, SkipReason.None, DiscoverySource.SiteVersion)
        ], TestCrawls.Rows(path, URL_ROWS));
        Assert.Equal([$"http://www.example.com/shop/ | {(int)LinkKind.Redirect}"], TestCrawls.Rows(path,
            "SELECT f.url, u.found_via FROM urls u JOIN urls f ON f.id = u.found_on_id WHERE u.url = 'http://www.example.com/landing'"));
    }

    /// <summary>
    /// The site links to its www version, which the probe has fetched or is fetching. From then on it's the site's own:
    /// found by that link, at that depth, crawled in full, and counted under the URL limit, once. With a limit of two (the
    /// home page and www), the home page's other links are over it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProbeTheSiteLinksToBecomesTheSitesOwnAndTakesASlot(bool probeAnsweredFirst)
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/");
        var (options, link) = Site(start);
        var counters = new CrawlCounters();

        await using (var writer = CrawlWriter.Open(path, options with { MaxUrls = 2 }, resume: false, counters, NullLogger.Instance))
        {
            await SeedAsync(writer, link, start, ["https://www.example.com/"]);
            var claim = await writer.ClaimAsync(10, 10, CancellationToken.None);
            Assert.Equal(2, claim.Internal.Count);
            var probe = claim.Internal.Single(item => item.Source == DiscoverySource.SiteVersion);
            if (probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.Success);
            }

            await RecordAsync(writer, claim.Internal.Single(item => item.Source == DiscoverySource.Seed), FetchOutcome.Success,
                link("https://www.example.com/", LinkKind.Hyperlink), link("https://example.com/p1", LinkKind.Hyperlink), link("https://example.com/p2", LinkKind.Hyperlink));
            if (!probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.Success);
            }

            // Fetched again in full, without taking a second slot.
            Assert.Equal(["https://www.example.com/"], (await writer.ClaimAsync(10, 10, CancellationToken.None)).Internal.Select(item => item.Url));
        }

        Assert.Equal(
        [
            Row("https://example.com/", FetchMode.Crawl, 0, UrlState.Done, SkipReason.None, DiscoverySource.Seed),
            Row("https://example.com/p1", FetchMode.Crawl, 1, UrlState.Skipped, SkipReason.UrlLimit, DiscoverySource.Link),
            Row("https://example.com/p2", FetchMode.Crawl, 1, UrlState.Skipped, SkipReason.UrlLimit, DiscoverySource.Link),
            Row("https://www.example.com/", FetchMode.Crawl, 1, UrlState.Fetching, SkipReason.None, DiscoverySource.Link)
        ], TestCrawls.Rows(path, URL_ROWS));

        // The site's own now, found and, while its answer stood, fetched; then fetched again, so not fetched yet.
        Assert.Equal((2, 1), (counters.Current.InternalFound, counters.Current.InternalFetched));
        ProgressAgreement.AssertMatchesTheFile(counters, path);
    }

    /// <summary>
    /// A probe gets a single try, which failed here. Once the site links to it, it is one of the site's own URLs, so it is
    /// fetched again with the retries those get, whether the probe's failure was already stored or still on its way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProbeThatFailedIsFetchedAgainOnceTheSiteLinksToIt(bool probeAnsweredFirst)
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/");
        var (options, link) = Site(start);
        var counters = new CrawlCounters();

        await using (var writer = CrawlWriter.Open(path, options, resume: false, counters, NullLogger.Instance))
        {
            await SeedAsync(writer, link, start, ["https://www.example.com/"]);
            var claim = await writer.ClaimAsync(10, 10, CancellationToken.None);
            var probe = claim.Internal.Single(item => item.Source == DiscoverySource.SiteVersion);
            if (probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.NetworkError);
            }

            await RecordAsync(writer, claim.Internal.Single(item => item.Source == DiscoverySource.Seed), FetchOutcome.Success,
                link("https://www.example.com/", LinkKind.Hyperlink));
            if (!probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.NetworkError);
            }
        }

        Assert.Contains(Row("https://www.example.com/", FetchMode.Crawl, 1, UrlState.Queued, SkipReason.None, DiscoverySource.Link), TestCrawls.Rows(path, URL_ROWS));
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = 'https://www.example.com/'"));
        ProgressAgreement.AssertMatchesTheFile(counters, path);
    }

    /// <summary>
    /// The URL limit is reached before the site links to its www version, which a probe found doesn't answer. No slot is
    /// left, as for any URL the site finds then: it stays a probe, with the answer its probe got, and is never fetched
    /// again, so the site's own URLs found and fetched stay within the limit, whether the probe's answer came first or not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProbeTheSiteLinksToOnceTheLimitIsReachedStaysAProbe(bool probeAnsweredFirst)
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/");
        var (options, link) = Site(start);
        var counters = new CrawlCounters();

        await using (var writer = CrawlWriter.Open(path, options with { MaxUrls = 1 }, resume: false, counters, NullLogger.Instance))
        {
            // The home page takes the one slot; the probe, which takes none, is claimed next.
            await SeedAsync(writer, link, start, ["https://www.example.com/"]);
            var home = Assert.Single((await writer.ClaimAsync(10, 10, CancellationToken.None)).Internal);
            var probe = Assert.Single((await writer.ClaimAsync(10, 10, CancellationToken.None)).Internal);
            Assert.Equal(DiscoverySource.SiteVersion, probe.Source);
            if (probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.NetworkError);
            }

            await RecordAsync(writer, home, FetchOutcome.Success,
                link("https://www.example.com/", LinkKind.Hyperlink), link("https://example.com/p1", LinkKind.Hyperlink));
            if (!probeAnsweredFirst)
            {
                await RecordAsync(writer, probe, FetchOutcome.NetworkError);
            }

            Assert.Empty((await writer.ClaimAsync(10, 10, CancellationToken.None)).Internal);
        }

        Assert.Equal(
        [
            Row("https://example.com/", FetchMode.Crawl, 0, UrlState.Done, SkipReason.None, DiscoverySource.Seed),
            Row("https://example.com/p1", FetchMode.Crawl, 1, UrlState.Skipped, SkipReason.UrlLimit, DiscoverySource.Link),
            Row("https://www.example.com/", FetchMode.Check, 0, UrlState.Done, SkipReason.None, DiscoverySource.SiteVersion)
        ], TestCrawls.Rows(path, URL_ROWS));
        Assert.Equal((1, 1), (counters.Current.InternalFound, counters.Current.InternalFetched));
        ProgressAgreement.AssertMatchesTheFile(counters, path);
    }

    /// <summary>Resuming remembers which URLs only a probe found, so the site linking to one afterwards still makes it the site's own.</summary>
    [Fact]
    public async Task AResumedCrawlStillKnowsItsProbes()
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/");
        var (options, link) = Site(start);
        await using (var writer = CrawlWriter.Open(path, options, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            await SeedAsync(writer, link, start, ["https://www.example.com/"]);
            var claim = await writer.ClaimAsync(10, 10, CancellationToken.None);
            await RecordAsync(writer, claim.Internal.Single(item => item.Source == DiscoverySource.SiteVersion), FetchOutcome.Success);
        }

        var counters = new CrawlCounters();
        await using (var writer = CrawlWriter.Open(path, options, resume: true, counters, NullLogger.Instance))
        {
            var home = Assert.Single((await writer.ClaimAsync(10, 10, CancellationToken.None)).Internal);
            await RecordAsync(writer, home, FetchOutcome.Success, link("https://www.example.com/", LinkKind.Hyperlink));
        }

        Assert.Contains(Row("https://www.example.com/", FetchMode.Crawl, 1, UrlState.Queued, SkipReason.None, DiscoverySource.Link), TestCrawls.Rows(path, URL_ROWS));

        // Restored as it was, then the probe adopted and fetched again: the counts are the file's.
        ProgressAgreement.AssertMatchesTheFile(counters, path);
    }

    /// <summary>A probe that redirects to another site leaves it unchecked when the crawl leaves other sites unchecked.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProbeLeadingToAnotherSiteIsCheckedOnlyIfOtherSitesAre(bool checkExternalLinks)
    {
        var path = _files.NewPath();
        var start = new Uri("https://example.com/");
        var (options, link) = Site(start);
        await using (var writer = CrawlWriter.Open(path, options with { CheckExternalLinks = checkExternalLinks }, resume: false, new CrawlCounters(), NullLogger.Instance))
        {
            await SeedAsync(writer, link, start, ["https://www.example.com/"]);
            var claim = await writer.ClaimAsync(10, 10, CancellationToken.None);
            await RecordAsync(writer, claim.Internal.Single(item => item.Source == DiscoverySource.SiteVersion), FetchOutcome.Redirect,
                link("https://parked.example/", LinkKind.Redirect));
        }

        Assert.Contains(checkExternalLinks
                ? Row("https://parked.example/", FetchMode.Check, 0, UrlState.Queued, SkipReason.None, DiscoverySource.SiteVersion)
                : Row("https://parked.example/", FetchMode.Check, 0, UrlState.Skipped, SkipReason.ExternalsDisabled, DiscoverySource.SiteVersion),
            TestCrawls.Rows(path, URL_ROWS));
    }

    private const string URL_ROWS = "SELECT url, fetch_mode, depth, state, skip_reason, source FROM urls";

    private static string Row(string url, FetchMode mode, int depth, UrlState state, SkipReason skip, DiscoverySource source) =>
        $"{url} | {(int)mode} | {depth} | {(int)state} | {(int)skip} | {(int)source}";

    /// <summary>Crawl options for a site at <paramref name="start"/> kept to its folder, and a maker of its links.</summary>
    private static (CrawlOptions Options, Func<string, LinkKind, OutLink> Link) Site(Uri start)
    {
        var links = new OutLinkFactory(new CrawlScope(start, includeSubdomains: false, stayInStartFolder: true));
        return (new CrawlOptions { StartUrl = start.AbsoluteUri, Concurrency = 4, StayInStartFolder = true },
            (target, kind) => links.Create(new Uri(target), kind, LinkFlags.None, LinkPosition.Content, null));
    }

    /// <summary>The start URL, then the site-version probes, as a crawl seeds them.</summary>
    private static async Task SeedAsync(CrawlWriter writer, Func<string, LinkKind, OutLink> link, Uri start, IEnumerable<string> versions)
    {
        await writer.DiscoverAsync([link(start.AbsoluteUri, LinkKind.Hyperlink)], DiscoverySource.Seed, null, CancellationToken.None);
        await writer.DiscoverAsync([.. versions.Select(url => link(url, LinkKind.Hyperlink))], DiscoverySource.SiteVersion, null, CancellationToken.None);
    }

    private static ValueTask RecordAsync(CrawlWriter writer, FrontierItem item, FetchOutcome outcome, params OutLink[] links)
    {
        var response = new ResponseRow
        {
            UrlId = item.Id, FetchedAt = 0, Outcome = outcome, StatusCode = outcome == FetchOutcome.Redirect ? 301 : 200, IsHtml = outcome == FetchOutcome.Success
        };
        return writer.RecordAsync(new PageRecord(item, response, links, false, null, null), CancellationToken.None);
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, Concurrency = 4 };
}
