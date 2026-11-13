using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Tests.Export;

/// <summary>
/// The words an export of the sitemaps or the origins writes, as literals: each is what the screen's cell says for the
/// same values (the app's <c>sitemapWords.test.ts</c> and <c>robotsWords.test.ts</c> hold the screens to the same).
/// </summary>
public sealed class ListWordsTests
{
    private static readonly SitemapUrlCounts COUNTS = new(Listed: 10, Fetched: 8, Indexable: 6, NonIndexable: 2, RobotsDisallowed: 1, RedirectingOrBroken: 3);

    private static SitemapInfo Sitemap(int id, SitemapState state, int? parentId = null, bool isIndex = false, string? error = null, int? statusCode = 200, int entries = 10,
        SitemapSource? foundVia = SitemapSource.RobotsTxt, SitemapUrlCounts? urls = null) =>
        new(id, $"https://www.example.com/sitemap-{id}.xml", parentId, foundVia, state, statusCode, isIndex, entries, 0, error, urls ?? COUNTS);

    private static readonly SitemapSubtree NONE = new(0, 0, false);

    [Theory]
    [InlineData(SitemapState.Read, null, 200, 10, "Read")]
    [InlineData(SitemapState.Failed, "404 Not Found", 404, 0, "Couldn’t be read: 404 Not Found")]
    [InlineData(SitemapState.Failed, "HTTP 404", 404, 0, "Couldn’t be read: 404 Not Found")]
    [InlineData(SitemapState.Failed, "HTTP 520", 520, 0, "Couldn’t be read: HTTP 520")]
    [InlineData(SitemapState.Failed, "Timed out", null, 0, "Couldn’t be read: it timed out")]
    [InlineData(SitemapState.Failed, "DNS lookup failed", null, 0, "Couldn’t be read: the DNS name wasn’t found")]
    [InlineData(SitemapState.Failed, "Could not connect: No connection could be made because the target machine actively refused it.", null, 0,
        "Couldn’t be read: the connection was refused")]
    [InlineData(SitemapState.Failed, "TLS handshake failed: a corrupted frame was received.", null, 0, "Couldn’t be read: a secure connection couldn’t be made")]
    [InlineData(SitemapState.Failed, "Something nobody expected", 200, 0, "Couldn’t be read")]
    [InlineData(SitemapState.Failed, "Unexpected end of file has occurred. Line 17, position 5.", 200, 4, "Read in part: its XML is broken at line 17")]
    [InlineData(SitemapState.Failed, "Unexpected end of file. Line 1234, position 5.", 200, 4, "Read in part: its XML is broken at line 1,234")]
    [InlineData(SitemapState.NotRead, "Not read: the crawler reads at most 1,000 sitemaps", null, 0, "Left unread")]
    public void ASitemapsStateIsSaidAsTheListSaysIt(SitemapState state, string? error, int? status, int entries, string words)
    {
        var urls = entries == 0 ? new SitemapUrlCounts(0, 0, 0, 0) : COUNTS;
        Assert.Equal(words, SitemapListWords.State(Sitemap(1, state, error: error, statusCode: status, entries: entries, foundVia: SitemapSource.Index, urls: urls), crawling: false, NONE));
    }

    [Fact]
    public void TheDefaultLocationWithNothingThereIsNoSitemapThatFailed()
    {
        var nothing = Sitemap(1, SitemapState.Failed, error: "404 Not Found", statusCode: 404, entries: 0, foundVia: SitemapSource.DefaultLocation, urls: new SitemapUrlCounts(0, 0, 0, 0));
        Assert.Equal("None there: 404 Not Found", SitemapListWords.State(nothing, crawling: false, NONE));
        var forbidden = nothing with { StatusCode = 403, Error = "403 Forbidden" };
        Assert.Equal("Couldn’t be read: 403 Forbidden", SitemapListWords.State(forbidden, crawling: false, NONE));
    }

    [Fact]
    public void ASitemapNotReadYetIsOnItsWayWhileTheCrawlRuns()
    {
        var pending = Sitemap(1, SitemapState.Pending, statusCode: null, entries: 0);
        Assert.Equal("Not read yet", SitemapListWords.State(pending, crawling: true, NONE));
        Assert.Equal("Never read", SitemapListWords.State(pending, crawling: false, NONE));
        Assert.Null(SitemapListWords.Counts(pending, NONE));
    }

    [Theory]
    [InlineData(SitemapDiscovery.Done, false, "Read, 1 of its 2 sitemaps")]
    [InlineData(SitemapDiscovery.Unfinished, false, "Read, has 2 of its sitemaps")]
    [InlineData(SitemapDiscovery.Running, true, "Read, 2 sitemaps so far")]
    public void AnIndexWithoutEverySitemapUnderItReadHasNoCountsOfItsOwn(SitemapDiscovery discovery, bool crawling, string words)
    {
        List<SitemapInfo> sitemaps = [Sitemap(1, SitemapState.Read, isIndex: true), Sitemap(2, SitemapState.Read, parentId: 1), Sitemap(3, SitemapState.Pending, parentId: 1, statusCode: null)];
        var subtree = SitemapListWords.SubtreeOf(sitemaps, 1, discovery);
        Assert.Equal(new SitemapSubtree(2, 1, discovery != SitemapDiscovery.Done), subtree);
        Assert.Equal(words, SitemapListWords.State(sitemaps[0], crawling, subtree));
        Assert.Null(SitemapListWords.Counts(sitemaps[0], subtree));

        // A sitemap under it knows its own.
        Assert.Equal(new long[] { 10, 7, 1, 2, 6, 2, 3 }, SitemapListWords.Counts(sitemaps[1], SitemapListWords.SubtreeOf(sitemaps, 2, discovery)));
    }

    [Fact]
    public void AnIndexReadWholeIsReadAndCountsEverySitemapUnderIt()
    {
        List<SitemapInfo> sitemaps = [Sitemap(1, SitemapState.Read, isIndex: true), Sitemap(2, SitemapState.Read, parentId: 1), Sitemap(3, SitemapState.Read, parentId: 2)];
        var subtree = SitemapListWords.SubtreeOf(sitemaps, 1, SitemapDiscovery.Done);
        Assert.Equal(new SitemapSubtree(2, 2, false), subtree);
        Assert.Equal("Read", SitemapListWords.State(sitemaps[0], crawling: false, subtree));
        Assert.NotNull(SitemapListWords.Counts(sitemaps[0], subtree));

        // One left unread at the crawler's limits stands for any more like it.
        sitemaps[2] = Sitemap(3, SitemapState.NotRead, parentId: 2, statusCode: null, entries: 0);
        var limited = SitemapListWords.SubtreeOf(sitemaps, 1, SitemapDiscovery.Done);
        Assert.True(limited.More);
        Assert.Equal("Read, has 2 of its sitemaps", SitemapListWords.State(sitemaps[0], crawling: false, limited));

        // An index with none of its sitemaps.
        List<SitemapInfo> alone = [Sitemap(1, SitemapState.Read, isIndex: true)];
        Assert.Equal("Read, its sitemaps to come", SitemapListWords.State(alone[0], crawling: true, SitemapListWords.SubtreeOf(alone, 1, SitemapDiscovery.Running)));
        Assert.Equal("Read, none of its sitemaps", SitemapListWords.State(alone[0], crawling: false, SitemapListWords.SubtreeOf(alone, 1, SitemapDiscovery.Unfinished)));
    }

    [Theory]
    [InlineData(SitemapSource.RobotsTxt, "robots.txt")]
    [InlineData(SitemapSource.Options, "Crawl settings")]
    [InlineData(SitemapSource.DefaultLocation, "Default location")]
    [InlineData(SitemapSource.Index, "Sitemap index")]
    [InlineData(null, "Not known")]
    public void HowASitemapWasFoundIsSaidAsTheListSaysIt(SitemapSource? foundVia, string words) => Assert.Equal(words, SitemapListWords.FoundVia(foundVia));

    [Theory]
    [InlineData(RobotsState.Read, 79L, 0L, true, "Read")]
    [InlineData(RobotsState.Read, 0L, 0L, true, "Read: empty")]
    [InlineData(RobotsState.Read, 79L, 0L, false, "Read: rules ignored")]
    [InlineData(RobotsState.Missing, null, 0L, true, "Missing: nothing refused")]
    [InlineData(RobotsState.Missing, null, 3L, true, "Missing when last asked")]
    [InlineData(RobotsState.Unreachable, null, 3L, true, "Couldn’t be read: URLs refused")]
    [InlineData(RobotsState.Unreachable, null, 0L, false, "Couldn’t be read: ignored")]
    [InlineData(RobotsState.Unanswered, null, 0L, true, "No answer: crawled anyway")]
    [InlineData(RobotsState.Unanswered, null, 3L, true, "No answer when last asked")]
    [InlineData(RobotsState.Unanswered, null, 3L, false, "No answer: ignored")]
    [InlineData(null, null, 3L, true, "None kept")]
    public void AnOriginsAnswerIsSaidAsTheListSaysIt(RobotsState? state, long? bytes, long blocked, bool respected, string words) =>
        Assert.Equal(words, RobotsOriginWords.State(new RobotsOrigin("https://www.example.com", true, state, 200, null, null, bytes, blocked), respected));
}
