using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Robots.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using static ShoutingIguana.Engine.Tests.Support.GridQueries;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The sitemaps and robots.txt of the synthetic site's crawl, as their screens read them: each sitemap with how it was
/// found and the URLs it lists, counted as the URL grid judges them, its URLs as a URL grid; each origin's robots.txt, line
/// by line, with the line that refused each blocked URL, by the crawler's own matcher.
/// </summary>
[Collection(GridCrawlCollection.NAME)]
public sealed class RobotsAndSitemapsReaderTests(GridCrawl crawl)
{
    /// <summary>The outcomes of a URL that redirects or is broken, as the Sitemaps screen's quick filter has them.</summary>
    private static readonly string[] BROKEN = ["redirect", "clientError", "serverError", "timeout", "networkError"];

    private string Site => crawl.Server.BaseUri.GetLeftPart(UriPartial.Authority);

    private SyntheticSite Planted => crawl.Server.Site;

    [Fact]
    public void TheSitemapsAreListedWithHowTheyWereFoundAndWhatTheyList()
    {
        var list = SitemapListReader.Read(crawl.Path);
        Assert.Equal(SitemapDiscovery.Done, list.Discovery);
        Assert.Equal(SummaryState.Present, list.Summary);

        // robots.txt declares /sitemap.xml, an index of the pages' sitemap, one of the pages only sitemaps list, and one that
        // doesn't exist.
        var index = list.Sitemaps[0];
        Assert.Equal(($"{Site}/sitemap.xml", null, SitemapSource.RobotsTxt, SitemapState.Read, 200, true, 0, 4, null),
            (index.Url, index.ParentId, index.FoundVia, index.State, index.StatusCode, index.IsIndex, index.Entries, index.Sitemaps, index.Error));
        Assert.Equal([$"{Site}/sitemaps/pages-0.xml", $"{Site}/sitemaps/extra.xml", $"{Site}/sitemaps/empty.xml", $"{Site}/sitemaps/missing.xml"],
            list.Sitemaps.Skip(1).Select(sitemap => sitemap.Url));
        Assert.All(list.Sitemaps.Skip(1), sitemap => Assert.Equal((index.Id, SitemapSource.Index, 0, false), (sitemap.ParentId, sitemap.FoundVia, sitemap.Sitemaps, sitemap.IsIndex)));
        var (pages, extra, missing) = (list.Sitemaps[1], list.Sitemaps[2], list.Sitemaps[4]);
        Assert.Equal((SitemapState.Failed, 404, "404 Not Found", new SitemapUrlCounts(0, 0, 0, 0)), (missing.State, missing.StatusCode, missing.Error, missing.Urls));
        // Every page, and the thin plant again (with a lastmod in the year 3000).
        Assert.Equal((SitemapState.Read, 200, null, GridCrawl.PAGES + 1), (pages.State, pages.StatusCode, pages.Error, pages.Entries));
        Assert.Equal((SitemapState.Read, TestCrawls.Count(crawl.Path, $"SELECT url_count FROM sitemaps WHERE id = {extra.Id}")), (extra.State, extra.Entries));
        Assert.True(extra.Entries >= Planted.GetExpectations().Orphans + 3);

        // Each sitemap's counts are of the URLs it lists, as the URL grid judges them; an index's are of every sitemap under it.
        foreach (var sitemap in list.Sitemaps)
        {
            var listed = Listed(crawl.Path, sitemap.Id == index.Id ? null : sitemap.Id);
            Assert.Equal(new SitemapUrlCounts(listed.Count, listed.Count(id => Row(id, "fetched") is true), listed.Count(id => Row(id, "indexable") is true),
                listed.Count(id => Row(id, "indexable") is false), listed.Count(id => Row(id, "outcome") is "robotsDisallowed"),
                listed.Count(id => Row(id, "outcome") is string outcome && BROKEN.Contains(outcome))), sitemap.Urls);
        }

        // The extra sitemap lists a URL robots.txt disallows, and two on another site that redirect and are gone: the
        // sitemap they're in says so, and so does the index over it. The pages' sitemap has none of either.
        Assert.Equal((1, 2, 0, 0), (extra.Urls.RobotsDisallowed, extra.Urls.RedirectingOrBroken, pages.Urls.RobotsDisallowed, pages.Urls.RedirectingOrBroken));
        Assert.Equal((1, 2), (index.Urls.RobotsDisallowed, index.Urls.RedirectingOrBroken));

        // The thin plant is in both, and the index counts it once.
        Assert.Equal(pages.Urls.Listed + extra.Urls.Listed - 1, index.Urls.Listed);
        Assert.True(pages.Urls.NonIndexable > 0 && pages.Urls.Indexable > pages.Urls.NonIndexable && extra.Urls.Fetched == extra.Urls.Listed);

        // Every count opens its URLs in the sitemap's grid, with or without a URL summary.
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            var read = SitemapListReader.Read(path);
            Assert.Equal(path == crawl.Path, read.Summary.Available);
            foreach (var sitemap in read.Sitemaps)
            {
                long Opens(params UrlFilter[] filters) => UrlGridReader.QuerySitemap(path, sitemap.Id, new UrlQuery { Filters = filters, Limit = 1 })!.Matching;
                Assert.Equal(sitemap.Urls, new SitemapUrlCounts(Opens(), Opens(Boolean("fetched", true)), Opens(Boolean("indexable", true)), Opens(Boolean("indexable", false)),
                    Opens(Names("outcome", FilterOperator.In, "robotsDisallowed")), Opens(Names("outcome", FilterOperator.In, BROKEN))));
            }

            Assert.Equal(list.Sitemaps.Select(sitemap => sitemap.Urls.Listed), read.Sitemaps.Select(sitemap => sitemap.Urls.Listed));
        }
    }

    /// <summary>
    /// Redirecting or broken is a URL that answered with a redirect or an error, or didn't answer: of six of a sitemap's
    /// pages given other ends, the server error, the timeout and the network error count, and the firewall's block, the
    /// one robots.txt refused and the one never asked don't, since none of those is known to be broken. The one
    /// robots.txt refused has its own count. Each count is its rows in the sitemap's grid, with the URL summary (the
    /// crawl analysed again, once the pages have their new ends) and without (continued since).
    /// </summary>
    [Fact]
    public async Task ASitemapsBrokenUrlsAreThoseThatRedirectErrorOrDontAnswer()
    {
        var copy = crawl.Copy();
        var before = SitemapListReader.Read(copy).Sitemaps[1].Urls;
        var pages = TestCrawls.Rows(copy, "SELECT printf('%09d', m.url_id) FROM sitemap_urls m JOIN responses r ON r.url_id = m.url_id WHERE m.sitemap_id = 2 AND r.outcome = 0 ORDER BY m.url_id LIMIT 6")
            .Select(int.Parse).ToList();
        (FetchOutcome Outcome, int? Status, string? Error)[] ends =
        [
            (FetchOutcome.ServerError, 500, null), (FetchOutcome.Timeout, null, "Timed out"), (FetchOutcome.NetworkError, null, "Connection refused"),
            (FetchOutcome.Blocked, 403, null), (FetchOutcome.RobotsDisallowed, null, null), (FetchOutcome.NetworkError, null, "Not checked: https://dead.example stopped answering")
        ];
        foreach (var (page, end) in pages.Zip(ends))
        {
            Change(copy, "UPDATE responses SET outcome = $outcome, status_code = $status, error = $error WHERE url_id = $id",
                ("$outcome", (int)end.Outcome), ("$status", (object?)end.Status ?? DBNull.Value), ("$error", (object?)end.Error ?? DBNull.Value), ("$id", page));
        }

        var continued = crawl.Copy();
        File.Copy(copy, continued, overwrite: true);
        Stamp(continued);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(copy);
        foreach (var path in new[] { copy, continued })
        {
            var read = SitemapListReader.Read(path);
            Assert.Equal(path == copy, read.Summary.Available);
            var urls = read.Sitemaps[1].Urls;
            Assert.Equal((before.RobotsDisallowed + 1, before.RedirectingOrBroken + 3), (urls.RobotsDisallowed, urls.RedirectingOrBroken));
            long Opens(params string[] outcomes) =>
                UrlGridReader.QuerySitemap(path, read.Sitemaps[1].Id, new UrlQuery { Filters = [Names("outcome", FilterOperator.In, outcomes)], Limit = 1 })!.Matching;
            Assert.Equal((urls.RobotsDisallowed, urls.RedirectingOrBroken), (Opens("robotsDisallowed"), Opens(BROKEN)));

            // The index over it counts them too.
            Assert.Equal(urls.RedirectingOrBroken + read.Sitemaps[2].Urls.RedirectingOrBroken, read.Sitemaps[0].Urls.RedirectingOrBroken);
            Assert.Equal(urls.RobotsDisallowed + read.Sitemaps[2].Urls.RobotsDisallowed, read.Sitemaps[0].Urls.RobotsDisallowed);
        }
    }

    [Fact]
    public void ASitemapSaysWhatBecameOfItAndWhereItCameFrom()
    {
        var index = SitemapListReader.Read(crawl.Path).Sitemaps[0];
        SitemapList After(string sql, params (string Name, object Value)[] parameters)
        {
            var copy = crawl.Copy();
            Change(copy, sql, parameters);
            return SitemapListReader.Read(copy);
        }

        // A URL two sitemaps list counts once under their index: here the URL robots.txt refused and the two that redirect
        // or are gone, listed by the pages' sitemap too, and seven of its pages by the extra one.
        var twice = After("""
            INSERT INTO sitemap_urls (sitemap_id, url_id) SELECT 3, url_id FROM sitemap_urls WHERE sitemap_id = 2 ORDER BY url_id LIMIT 7;
            INSERT INTO sitemap_urls (sitemap_id, url_id) SELECT 2, m.url_id FROM sitemap_urls m JOIN responses r ON r.url_id = m.url_id WHERE m.sitemap_id = 3 AND r.outcome <> 0
            """);
        Assert.Equal(index.Urls, twice.Sitemaps[0].Urls);
        Assert.Equal((1, 2), (twice.Sitemaps[1].Urls.RobotsDisallowed, twice.Sitemaps[1].Urls.RedirectingOrBroken));
        Assert.Equal(SitemapListReader.Read(crawl.Path).Sitemaps[2].Urls.Listed + 7, twice.Sitemaps[2].Urls.Listed);

        // How it was found is read from the stored robots.txt and the crawl's options, as the crawler's discovery reads them.
        Assert.Null(After("UPDATE robots SET content = 'User-agent: *' WHERE origin = $site", ("$site", Site)).Sitemaps[0].FoundVia);
        Assert.Equal(SitemapSource.RobotsTxt, After("UPDATE robots SET content = 'Sitemap: /sitemap.xml' WHERE origin = $site", ("$site", Site)).Sitemaps[0].FoundVia);
        Assert.Null(After("UPDATE robots SET status_code = 503 WHERE origin = $site", ("$site", Site)).Sitemaps[0].FoundVia);
        Assert.Equal(SitemapSource.Options, After("""
            UPDATE robots SET content = '' WHERE origin = $site;
            UPDATE crawl SET options_json = json_set(options_json, '$.extraSitemaps', json_array($site || '/sitemap.xml'));
            """, ("$site", Site)).Sitemaps[0].FoundVia);
        Assert.Equal(SitemapSource.DefaultLocation, After("UPDATE sitemaps SET is_probe = 1 WHERE id = 1").Sitemaps[0].FoundVia);

        // What became of it: read, failed, left unread at the crawler's limits, or not read yet.
        var states = After("""
            UPDATE sitemaps SET status_code = NULL, error = NULL WHERE id = 2;
            UPDATE sitemaps SET status_code = NULL, error = 'Not read: the crawler reads at most 1,000 sitemaps' WHERE id = 3;
            UPDATE sitemaps SET status_code = 200, error = 'Unexpected end of file' WHERE id = 4;
            """);
        Assert.Equal([SitemapState.Read, SitemapState.Pending, SitemapState.NotRead, SitemapState.Failed, SitemapState.Failed], states.Sitemaps.Select(sitemap => sitemap.State));

        // How far the crawl got with them.
        Assert.Equal(SitemapDiscovery.Unfinished, After("UPDATE crawl SET sitemaps_done = 0").Discovery);
        Assert.Equal(SitemapDiscovery.Off, After("UPDATE crawl SET options_json = json_set(options_json, '$.discoverSitemaps', json('false'))").Discovery);
        var none = After("DELETE FROM sitemaps; DELETE FROM sitemap_urls");
        Assert.Empty(none.Sitemaps);
    }

    [Fact]
    public void ASitemapsUrlsAreTheUrlGridNarrowedToWhatItLists()
    {
        var sitemaps = SitemapListReader.Read(crawl.Path).Sitemaps;
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            foreach (var sitemap in sitemaps)
            {
                var listed = Listed(crawl.Path, sitemap.ParentId is null ? null : sitemap.Id);
                var page = UrlGridReader.QuerySitemap(path, sitemap.Id, new UrlQuery { Columns = ["url", "statusCode", "indexable"], Limit = 1_000 })!;
                Assert.Equal(listed.Order(), page.Rows.Select(row => row.Id));
                Assert.Equal((listed.Count, listed.Count), (page.Matching, page.Total));
                Assert.All(page.Rows, row => Assert.Equal(Row(row.Id, "url"), row.Values[1]));

                // Filtered, searched and sorted as the URL grid is, the total still the sitemap's.
                var filters = new[] { Boolean("indexable", false) };
                var expected = crawl.Reference.Order([new UrlSort("url", true)], crawl.Reference.Matching(filters, "/p/").Where(listed.Contains));
                var query = new UrlQuery { Filters = filters, Search = "/p/", Sort = [new UrlSort("url", true)], Limit = 5 };
                var first = UrlGridReader.QuerySitemap(path, sitemap.Id, query)!;
                Assert.Equal((expected.Count, listed.Count), (first.Matching, first.Total));
                List<int> read = [.. first.Rows.Select(row => row.Id)];
                for (var next = first.Next; next is not null;)
                {
                    var more = UrlGridReader.QuerySitemap(path, sitemap.Id, query with { After = next })!;
                    read.AddRange(more.Rows.Select(row => row.Id));
                    next = more.Next;
                }

                Assert.Equal(expected, read);
            }
        }

        // The check filter narrows it further; a column that needs the analysis is refused without one, as in the URL grid.
        var flagged = UrlGridReader.QuerySitemap(crawl.Path, sitemaps[0].Id, new UrlQuery { Filters = [Names("check", FilterOperator.In, "sitemap-non-indexable")], Limit = 1 })!;
        Assert.Equal(crawl.Reference.Issues.Count(entry => entry.Value.Any(issue => issue.Issue == "sitemap-non-indexable")), flagged.Matching);
        Assert.Equal(["inlinks"], Assert.Throws<NeedsAnalysisException>(() =>
            UrlGridReader.QuerySitemap(crawl.Changed, sitemaps[0].Id, new UrlQuery { Sort = [new UrlSort("inlinks")] })).Columns);
        Assert.Null(UrlGridReader.QuerySitemap(crawl.Path, 999, new UrlQuery()));
        Assert.Contains("limit", Assert.Throws<UrlQueryException>(() => UrlGridReader.QuerySitemap(crawl.Path, 1, new UrlQuery { Limit = 0 })).Errors.Keys);

        // A cursor of one sitemap's grid isn't another's.
        var cursor = UrlGridReader.QuerySitemap(crawl.Path, sitemaps[1].Id, new UrlQuery { Limit = 5 })!.Next!;
        Assert.Contains("after", Assert.Throws<UrlQueryException>(() => UrlGridReader.QuerySitemap(crawl.Path, sitemaps[2].Id, new UrlQuery { After = cursor })).Errors.Keys);
    }

    [Fact]
    public void RobotsTxtIsShownWithTheLineThatBlockedEachUrl()
    {
        // The site's own robots.txt refuses its private area; the partner site's refuses one of its own pages.
        var refused = crawl.Reference.Rows.Where(row => (string?)row.Value["outcome"] == "robotsDisallowed").ToList();
        var blocked = refused.Where(row => (bool)row.Value["isInternal"]!).Select(row => row.Key).Order().ToList();
        var elsewhere = refused.Where(row => !(bool)row.Value["isInternal"]!).Select(row => row.Key).ToList();
        Assert.Equal((Planted.GetExpectations().RobotsDisallowed, Planted.GetExpectations().PartnerRobotsDisallowed), (blocked.Count, elsewhere.Count));
        Assert.True(blocked.Count > 2);

        var origins = RobotsReader.ReadOrigins(crawl.Path);
        Assert.Equal((TestCrawls.Count(crawl.Path, "SELECT COUNT(*) FROM robots"), 0, true, "ShoutingIguana", blocked.Count + elsewhere.Count),
            (origins.Total, origins.Offset, origins.Respected, origins.UserAgent, origins.Blocked));
        var site = origins.Origins[0];
        var content = TestCrawls.Rows(crawl.Path, $"SELECT content FROM robots WHERE origin = '{Site}'").Single();
        Assert.Equal((Site, true, RobotsState.Read, 200, null, System.Text.Encoding.UTF8.GetByteCount(content), blocked.Count),
            (site.Origin, site.IsInternal, site.State, site.StatusCode, site.Error, site.Bytes, site.Blocked));
        Assert.InRange(site.FetchedAt!.Value, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        // The site's other scheme never answered the crawler's probe of it, and isn't the start URL's origin: the crawl gave
        // no verdict and went ahead, which the answer says. It's the site's own, and comes next.
        var twin = origins.Origins[1];
        Assert.Equal(("https" + Site[4..], true, RobotsState.Unanswered, null, null, 0), (twin.Origin, twin.IsInternal, twin.State, twin.StatusCode, twin.Bytes, twin.Blocked));
        Assert.False(string.IsNullOrEmpty(twin.Error));

        // The partner site's is read, and blocked its one page. The list pages.
        var partner = Assert.Single(origins.Origins, origin => !origin.IsInternal);
        var partnerContent = TestCrawls.Rows(crawl.Path, $"SELECT content FROM robots WHERE origin = '{partner.Origin}'").Single();
        Assert.Equal((RobotsState.Read, 200, System.Text.Encoding.UTF8.GetByteCount(partnerContent), (long)elsewhere.Count),
            (partner.State, partner.StatusCode, partner.Bytes, partner.Blocked));
        Assert.Equal([partner], RobotsReader.ReadOrigins(crawl.Path, offset: 1, limit: 5).Origins.Where(origin => !origin.IsInternal));
        Assert.Equal(site, Assert.Single(RobotsReader.ReadOrigins(crawl.Path, limit: 1).Origins));
        Assert.Contains("limit", Assert.Throws<UrlQueryException>(() => RobotsReader.ReadOrigins(crawl.Path, limit: 0)).Errors.Keys);

        // The file, line by line: what each is, that the * group is the crawl's, and that its one rule refused every blocked URL.
        var file = RobotsReader.ReadFile(crawl.Path, Site)!;
        Assert.Equal((site, "ShoutingIguana", new RobotsUnmatched(0, 0)), (file.Origin, file.UserAgent, file.Unmatched));
        Assert.Equal(
        [
            new RobotsFileLine(1, "User-agent: *", RobotsLineKind.UserAgent, true, null, null),
            new RobotsFileLine(2, "Disallow: /private/", RobotsLineKind.Disallow, true, blocked.Count, null),
            new RobotsFileLine(3, "", RobotsLineKind.Blank, null, null, null),
            new RobotsFileLine(4, $"Sitemap: {Site}/sitemap.xml", RobotsLineKind.Sitemap, true, null, null)
        ], file.Lines);
        Assert.Equal(content, string.Join("\n", file.Lines.Select(line => line.Text)) + "\n");

        // The blocked URLs, in crawl order, each with the line and its text; narrowed to a line or a reason; paged.
        var all = RobotsReader.ReadBlocked(crawl.Path, Site)!;
        Assert.Equal((Site, blocked.Count, blocked.Count, 0), (all.Origin, all.Total, all.Matching, all.Offset));
        Assert.Equal(blocked.Select(id => new RobotsBlockedUrl(id, (string)Row(id, "url")!, RobotsBlockReason.Rule, 2, "Disallow: /private/", null)), all.Rows);
        Assert.All(all.Rows, row => Assert.StartsWith($"{Site}/private/", row.Url, StringComparison.Ordinal));
        Assert.Equal(all.Rows, RobotsReader.ReadBlocked(crawl.Path, Site, line: 2, reason: RobotsBlockReason.Rule)!.Rows);
        Assert.Equal((blocked.Count, 0), (RobotsReader.ReadBlocked(crawl.Path, Site, line: 1)!.Total, RobotsReader.ReadBlocked(crawl.Path, Site, line: 1)!.Matching));
        Assert.Empty(RobotsReader.ReadBlocked(crawl.Path, Site, reason: RobotsBlockReason.Unknown)!.Rows);
        Assert.Equal(all.Rows.Skip(1).Take(2), RobotsReader.ReadBlocked(crawl.Path, Site, offset: 1, limit: 2)!.Rows);
        Assert.Equal(elsewhere, RobotsReader.ReadBlocked(crawl.Path, partner.Origin)!.Rows.Select(row => row.Id));

        Assert.Null(RobotsReader.ReadFile(crawl.Path, "https://elsewhere.example"));
        Assert.Null(RobotsReader.ReadBlocked(crawl.Path, "https://elsewhere.example"));

        // The same without a URL summary: nothing here reads it.
        Assert.Equal(all.Rows, RobotsReader.ReadBlocked(crawl.Changed, Site)!.Rows);
    }

    /// <summary>
    /// The origins come as the screen lists them: the start URL's, then the site's others, then every other origin by the
    /// URLs it blocked, most first, then by name, whether or not the crawl kept a robots.txt for it.
    /// </summary>
    [Fact]
    public void TheOriginsComeTheSitesOwnFirstThenByWhatTheyBlocked()
    {
        var copy = crawl.Copy();
        var twin = "https" + Site[4..];
        var partner = RobotsReader.ReadOrigins(crawl.Path).Origins.Single(origin => !origin.IsInternal).Origin;

        // Other sites, some with a robots.txt of their own, and URLs the crawl was kept from on some of them: the partner
        // site's own robots.txt keeps it from one of its pages, and here five of the site's pages are stored as others', and
        // as refused.
        var moved = TestCrawls.Rows(copy, $"SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url LIKE '{Site}/p/1%' ORDER BY u.id LIMIT 5").Select(int.Parse).ToList();
        Assert.Equal(5, moved.Count);
        string[] theirs = ["https://m.example", "https://m.example", "https://m.example", "https://z.example", "https://b.example"];
        for (var index = 0; index < moved.Count; index++)
        {
            Change(copy, "UPDATE urls SET url = $url WHERE id = $id", ("$url", $"{theirs[index]}/{index}"), ("$id", moved[index]));
            Change(copy, $"UPDATE responses SET outcome = {(int)ShoutingIguana.Engine.Fetching.Models.FetchOutcome.RobotsDisallowed}, error = NULL WHERE url_id = $id", ("$id", moved[index]));
        }

        Change(copy, """
            INSERT INTO robots (origin, status_code, content, error, fetched_at)
            VALUES ('https://b.example', 200, 'User-agent: *' || char(10) || 'Disallow: /', NULL, 0), ('https://c.example', 404, NULL, NULL, 0), ('https://a.example', 503, NULL, NULL, 0)
            """);
        var site = RobotsReader.ReadOrigins(crawl.Path).Origins[0].Blocked;
        var origins = RobotsReader.ReadOrigins(copy);
        Assert.Equal(
        [
            (Site, true, site), (twin, true, 0), ("https://m.example", false, 3), (partner, false, 1), ("https://b.example", false, 1),
            ("https://z.example", false, 1), ("https://a.example", false, 0), ("https://c.example", false, 0)
        ], origins.Origins.Select(origin => (origin.Origin, origin.IsInternal, origin.Blocked)));
        Assert.Equal((8L, site + 6), (origins.Total, origins.Blocked));
        Assert.Equal([RobotsState.Read, RobotsState.Unanswered, null, RobotsState.Read, RobotsState.Read, null, RobotsState.Unreachable, RobotsState.Missing], origins.Origins.Select(origin => origin.State));

        // And paged in that order.
        Assert.Equal(origins.Origins.Skip(2).Take(3), RobotsReader.ReadOrigins(copy, offset: 2, limit: 3).Origins);
    }

    /// <summary>
    /// The rule that refused a URL is the one the crawler's matcher gives for the stored file: the longest that matches, for
    /// the group that applies to the crawler. Where no line accounts for a blocked URL, it says why.
    /// </summary>
    [Fact]
    public void ABlockedUrlsRuleIsTheMatchersAndWhereThereIsNoneItSaysWhy()
    {
        var blocked = RobotsReader.ReadBlocked(crawl.Path, Site, limit: 1_000)!.Rows;
        string With(string sql, params (string Name, object Value)[] parameters)
        {
            var copy = crawl.Copy();
            Change(copy, sql, [.. parameters, ("$site", Site)]);
            return copy;
        }

        // A longer rule wins, and an allow of the same length; a group that names the crawler applies in place of *.
        var specific = With("UPDATE robots SET content = $content WHERE origin = $site",
            ("$content", "# The shop\r\nUser-agent: *\r\nDisallow: /private/\r\nDisallow: /private/1\r\nAllow: /private/2$\r\nDisallow: /private/2$\r\nnonsense\r\nHost: example.com\r\n"));
        var rows = RobotsReader.ReadBlocked(specific, Site, limit: 1_000)!.Rows;
        Assert.All(rows, row => Assert.Equal(row.Url.EndsWith("/private/2", StringComparison.Ordinal)
                ? (RobotsBlockReason.Unknown, null, null)
                : row.Url.Contains("/private/1", StringComparison.Ordinal) ? (RobotsBlockReason.Rule, 4, "Disallow: /private/1") : (RobotsBlockReason.Rule, (int?)3, "Disallow: /private/"),
            (row.Reason, row.Line, row.Rule)));
        Assert.Contains(rows, row => row.Line == 4);
        Assert.Contains(rows, row => row.Reason == RobotsBlockReason.Unknown);
        var file = RobotsReader.ReadFile(specific, Site)!;
        Assert.Equal(
            [RobotsLineKind.Comment, RobotsLineKind.UserAgent, RobotsLineKind.Disallow, RobotsLineKind.Disallow, RobotsLineKind.Allow, RobotsLineKind.Disallow, RobotsLineKind.Invalid, RobotsLineKind.Other],
            file.Lines.Select(line => line.Kind));
        Assert.Equal([null, null, rows.Count(row => row.Line == 3), rows.Count(row => row.Line == 4), null, 0, null, null], file.Lines.Select(line => line.Blocked));
        Assert.Equal([null, null, null, null, null, null, RobotsLineProblem.NotADirective, RobotsLineProblem.UnknownDirective], file.Lines.Select(line => line.Problem));
        Assert.Equal(new RobotsUnmatched(0, rows.Count(row => row.Reason == RobotsBlockReason.Unknown)), file.Unmatched);
        Assert.Equal(rows.Count, file.Lines.Sum(line => line.Blocked ?? 0) + file.Unmatched.Unknown);

        var named = With("UPDATE robots SET content = $content WHERE origin = $site", ("$content", "User-agent: *\nDisallow: /\n\nUser-agent: shoutingiguana\nDisallow: /private/\n"));
        Assert.Equal([false, false, null, true, true], RobotsReader.ReadFile(named, Site)!.Lines.Select(line => line.Applies));
        Assert.All(RobotsReader.ReadBlocked(named, Site, limit: 1_000)!.Rows, row => Assert.Equal(5, row.Line));

        // The file was read again by a later run, and no longer refuses them.
        var changed = With("UPDATE robots SET content = 'User-agent: *' || char(10) || 'Disallow: /nothing/' WHERE origin = $site");
        Assert.All(RobotsReader.ReadBlocked(changed, Site, limit: 1_000)!.Rows, row => Assert.Equal((RobotsBlockReason.Unknown, null), (row.Reason, row.Line)));
        Assert.Equal((new RobotsUnmatched(0, blocked.Count), 0L), (RobotsReader.ReadFile(changed, Site)!.Unmatched, RobotsReader.ReadFile(changed, Site)!.Lines[1].Blocked));

        // robots.txt couldn't be read when a URL came up: the crawl stored how with the URL.
        var one = blocked[1].Id;
        var failedOnce = With("UPDATE responses SET error = 'robots.txt unreachable: HTTP 503' WHERE url_id = $id", ("$id", one));
        Assert.Equal(new RobotsBlockedUrl(one, blocked[1].Url, RobotsBlockReason.Unreachable, null, null, "HTTP 503"),
            Assert.Single(RobotsReader.ReadBlocked(failedOnce, Site, reason: RobotsBlockReason.Unreachable)!.Rows));
        Assert.Equal((new RobotsUnmatched(1, 0), blocked.Count - 1), (RobotsReader.ReadFile(failedOnce, Site)!.Unmatched, RobotsReader.ReadFile(failedOnce, Site)!.Lines[1].Blocked));

        // Or the origin's own answer says so, for a crawl by an engine that didn't: the file has no lines to show.
        var unreachable = With("UPDATE robots SET status_code = 503, content = NULL WHERE origin = $site");
        var failed = RobotsReader.ReadFile(unreachable, Site)!;
        Assert.Equal((RobotsState.Unreachable, 503, null, 0, new RobotsUnmatched(blocked.Count, 0)), (failed.Origin.State, failed.Origin.StatusCode, failed.Origin.Bytes, failed.Lines.Count, failed.Unmatched));
        Assert.All(RobotsReader.ReadBlocked(unreachable, Site, limit: 1_000)!.Rows, row => Assert.Equal((RobotsBlockReason.Unreachable, "503 Service Unavailable"), (row.Reason, row.Error)));
        var unanswered = RobotsReader.ReadOrigins(With("UPDATE robots SET status_code = NULL, content = NULL, error = 'Timed out' WHERE origin = $site")).Origins[0];
        Assert.Equal((RobotsState.Unreachable, "Timed out"), (unanswered.State, unanswered.Error));
        Assert.Equal(RobotsState.Missing, RobotsReader.ReadOrigins(With("UPDATE robots SET status_code = NULL, content = NULL, error = 'More than 5 redirects' WHERE origin = $site")).Origins[0].State);

        // What an answer meant to the crawl, by status: a 429 is a server's "not now", which refuses as a 5xx does, on any
        // origin; every other 4xx means there are no rules.
        Assert.Equal(
            [RobotsState.Read, RobotsState.Read, RobotsState.Missing, RobotsState.Missing, RobotsState.Missing, RobotsState.Unreachable, RobotsState.Unreachable, RobotsState.Unreachable, RobotsState.Unreachable],
            new int?[] { 200, 204, 404, 403, 410, 429, 500, 503, 301 }.Select(status => RobotsReader.StateOf(status, null)));
        Assert.Equal([RobotsState.Unreachable, RobotsState.Unreachable, RobotsState.Missing], new int?[] { 429, 503, 404 }.Select(status => RobotsReader.StateOf(status, null, onStartOrigin: false)));
        foreach (var (status, state) in new[] { (429, RobotsState.Unreachable), (430, RobotsState.Missing), (428, RobotsState.Missing) })
        {
            var answered = RobotsReader.ReadFile(With("UPDATE robots SET status_code = $status, content = NULL WHERE origin = $site", ("$status", status)), Site)!;
            Assert.Equal((state, status), (answered.Origin.State, answered.Origin.StatusCode));
            Assert.Equal(state == RobotsState.Unreachable ? new RobotsUnmatched(blocked.Count, 0) : new RobotsUnmatched(0, blocked.Count), answered.Unmatched);
        }

        // No answer at all. From the start URL's origin, the crawl refused everything there. From any other, the site's own
        // other versions included, it gave no verdict and went ahead: nothing was refused, and the answer says which.
        var twin = (Site.StartsWith("https:", StringComparison.Ordinal) ? "http:" : "https:") + Site[Site.IndexOf(':', StringComparison.Ordinal)..][1..];
        var silent = With("""
            UPDATE robots SET status_code = NULL, content = NULL, error = 'Connection refused' WHERE origin <> $site;
            INSERT OR REPLACE INTO robots (origin, status_code, content, error, fetched_at) VALUES ($twin, NULL, NULL, 'Connection refused', 0), ('https://failing.example', 503, NULL, NULL, 0);
            """, ("$twin", twin));
        var origins = RobotsReader.ReadOrigins(silent).Origins;
        Assert.Equal((RobotsState.Read, true), (origins[0].State, origins[0].IsInternal));
        Assert.Equal((twin, true, RobotsState.Unanswered, "Connection refused", 0L), (origins[1].Origin, origins[1].IsInternal, origins[1].State, origins[1].Error, origins[1].Blocked));
        Assert.Equal(RobotsState.Unreachable, origins.Single(origin => origin.Origin == "https://failing.example").State);
        Assert.All(origins.Skip(2).Where(origin => origin.Origin != "https://failing.example"), origin => Assert.Equal((false, RobotsState.Unanswered, null), (origin.IsInternal, origin.State, origin.StatusCode)));
        Assert.True(origins.Count > 3);
        var twinFile = RobotsReader.ReadFile(silent, twin)!;
        Assert.Equal((RobotsState.Unanswered, 0, new RobotsUnmatched(0, 0)), (twinFile.Origin.State, twinFile.Lines.Count, twinFile.Unmatched));

        // An origin the file keeps no robots.txt for still lists what was refused there.
        var gone = RobotsReader.ReadOrigins(With("DELETE FROM robots WHERE origin = $site"));
        Assert.Equal((Site, null, blocked.Count, true), (gone.Origins[0].Origin, gone.Origins[0].State, gone.Origins[0].Blocked, gone.Origins[0].IsInternal));

        // A crawl told to ignore robots.txt says so.
        Assert.False(RobotsReader.ReadOrigins(With("UPDATE crawl SET options_json = json_set(options_json, '$.respectRobotsTxt', json('false'))")).Respected);
    }

    /// <summary>
    /// The origins found by a text in their name and narrowed to those that blocked something, or nothing, in the list's
    /// own order, paged; with the counts a filter bar shows: of the origins the text finds, how many blocked something and
    /// how many didn't, each the rows that filter gives. The crawl's own totals stay the crawl's.
    /// </summary>
    [Fact]
    public void TheOriginsAreSearchedAndFilteredWithTheCountsAFilterBarShows()
    {
        var copy = crawl.Copy();
        var moved = TestCrawls.Rows(copy, $"SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE u.url LIKE '{Site}/p/1%' ORDER BY u.id LIMIT 5").Select(int.Parse).ToList();
        string[] theirs = ["https://m.example", "https://m.example", "https://m.example", "https://z.example", "https://b.example"];
        for (var index = 0; index < moved.Count; index++)
        {
            Change(copy, "UPDATE urls SET url = $url WHERE id = $id", ("$url", $"{theirs[index]}/{index}"), ("$id", moved[index]));
            Change(copy, $"UPDATE responses SET outcome = {(int)FetchOutcome.RobotsDisallowed}, error = NULL WHERE url_id = $id", ("$id", moved[index]));
        }

        Change(copy, """
            INSERT INTO robots (origin, status_code, content, error, fetched_at)
            VALUES ('https://b.example', 200, 'User-agent: *' || char(10) || 'Disallow: /', NULL, 0), ('https://c.example', 404, NULL, NULL, 0), ('https://a.example', 503, NULL, NULL, 0)
            """);
        var all = RobotsReader.ReadOrigins(copy);
        Assert.Equal(8, all.Total);
        var blocking = all.Origins.Where(origin => origin.Blocked > 0).ToList();
        var partner = all.Origins.Single(origin => origin is { IsInternal: false, Blocked: 1 } && !origin.Origin.EndsWith(".example", StringComparison.Ordinal)).Origin;
        Assert.Equal([Site, "https://m.example", partner, "https://b.example", "https://z.example"], blocking.Select(origin => origin.Origin));

        // With nothing asked, every origin matches, and the counts are the crawl's.
        Assert.Equal((8L, new RobotsOriginCounts(8, 5, 3)), (all.Matching, all.Counts));

        // Those that blocked something, and those that blocked nothing: the list's order, and what's left of it.
        RobotsOriginPage Read(RobotsOriginQuery query) => RobotsReader.ReadOrigins(copy, query);
        var blocked = Read(new RobotsOriginQuery { Blocked = true });
        Assert.Equal(blocking, blocked.Origins);
        Assert.Equal((8L, 5L, all.Blocked, new RobotsOriginCounts(8, 5, 3)), (blocked.Total, blocked.Matching, blocked.Blocked, blocked.Counts));
        var clear = Read(new RobotsOriginQuery { Blocked = false });
        Assert.Equal(all.Origins.Except(blocking), clear.Origins);
        Assert.Equal((3L, new RobotsOriginCounts(8, 5, 3)), (clear.Matching, clear.Counts));

        // Found by a text in the name, whatever its case: the counts are then of the origins the text finds, so each is
        // the rows its filter gives under the same text.
        var found = Read(new RobotsOriginQuery { Search = ".EXAMPLE" });
        Assert.Equal(["https://m.example", "https://b.example", "https://z.example", "https://a.example", "https://c.example"], found.Origins.Select(origin => origin.Origin));
        Assert.Equal((8L, 5L, new RobotsOriginCounts(5, 3, 2)), (found.Total, found.Matching, found.Counts));
        foreach (var search in new[] { ".example", "m.exa", "https://", Site, "nothing-has-this", "E" })
        {
            var searched = Read(new RobotsOriginQuery { Search = search, Limit = 1_000 });
            var expected = all.Origins.Where(origin => origin.Origin.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(expected, searched.Origins);
            Assert.Equal((expected.Count, new RobotsOriginCounts(expected.Count, expected.Count(origin => origin.Blocked > 0), expected.Count(origin => origin.Blocked == 0))),
                (searched.Matching, searched.Counts));
            Assert.Equal(searched.Counts.Blocked, Read(new RobotsOriginQuery { Search = search, Blocked = true }).Matching);
            Assert.Equal(searched.Counts.NotBlocked, Read(new RobotsOriginQuery { Search = search, Blocked = false }).Matching);
            Assert.Equal(expected.Where(origin => origin.Blocked > 0), Read(new RobotsOriginQuery { Search = search, Blocked = true, Limit = 1_000 }).Origins);
        }

        // Paged within what matches; past its end, no rows and the same counts.
        var paged = Read(new RobotsOriginQuery { Search = ".example", Blocked = true, Offset = 1, Limit = 1 });
        Assert.Equal(["https://b.example"], paged.Origins.Select(origin => origin.Origin));
        Assert.Equal((3L, 1), (paged.Matching, paged.Offset));
        Assert.Equal((0, 3L), (Read(new RobotsOriginQuery { Search = ".example", Blocked = true, Offset = 3 }).Origins.Count, Read(new RobotsOriginQuery { Search = ".example", Blocked = true, Offset = 3 }).Matching));

        Assert.Contains("search", Assert.Throws<UrlQueryException>(() => Read(new RobotsOriginQuery { Search = new string('a', 1_001) })).Errors.Keys);
        Assert.Contains("limit", Assert.Throws<UrlQueryException>(() => Read(new RobotsOriginQuery { Limit = 0 })).Errors.Keys);
        Assert.Contains("offset", Assert.Throws<UrlQueryException>(() => Read(new RobotsOriginQuery { Offset = -1 })).Errors.Keys);
    }

    /// <summary>An origin's file says whether the crawl obeyed robots.txt, as the list of origins does, so its page needn't ask the list.</summary>
    [Fact]
    public void AnOriginsFileSaysWhetherTheCrawlObeyedRobotsTxt()
    {
        Assert.True(RobotsReader.ReadFile(crawl.Path, Site)!.Respected);
        var ignored = crawl.Copy();
        Change(ignored, "UPDATE crawl SET options_json = json_set(options_json, '$.respectRobotsTxt', json('false'))");
        Assert.False(RobotsReader.ReadFile(ignored, Site)!.Respected);
        Assert.Equal(RobotsReader.ReadOrigins(ignored).Respected, RobotsReader.ReadFile(ignored, Site)!.Respected);
    }

    /// <summary>
    /// The inspector says of a URL robots.txt refused what the origin's list of blocked URLs says of it: the origin, why,
    /// and the line that refused it, so its page can open that file at that line. The list answers for one URL too.
    /// </summary>
    [Fact]
    public void TheInspectorNamesTheOriginAndTheLineThatRefusedAUrl()
    {
        var blocked = RobotsReader.ReadBlocked(crawl.Path, Site, limit: 1_000)!.Rows;
        Assert.True(blocked.Count > 2);
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            foreach (var row in blocked)
            {
                var test = RobotsTest(path, row.Id);
                Assert.Equal((TestOutcome.Fail, false, Site, RobotsBlockReason.Rule, 2, "Disallow: /private/"), (test.Outcome, test.Unreachable, test.Origin, test.Reason, test.Line, test.Rule));
                Assert.Equal((row.Reason, row.Line, row.Rule), (test.Reason, test.Line, test.Rule));

                // The list, asked for that URL alone: its one row, of the origin's total.
                var one = RobotsReader.ReadBlocked(path, Site, urlId: row.Id)!;
                Assert.Equal((blocked.Count, 1L), (one.Total, one.Matching));
                Assert.Equal([row], one.Rows);
            }
        }

        // A URL the crawl fetched names the origin whose robots.txt allowed it, and no reason; one on another site names
        // that site's. The list has no row for either.
        var home = UrlGridReader.Lookup(crawl.Path, Site + "/")!.Id;
        var allowed = RobotsTest(crawl.Path, home);
        Assert.Equal((TestOutcome.Pass, Site, null, null, null, null), (allowed.Outcome, allowed.Origin, allowed.Reason, allowed.Line, allowed.Rule, allowed.Unreachable));
        Assert.Equal((blocked.Count, 0L, 0), (RobotsReader.ReadBlocked(crawl.Path, Site, urlId: home)!.Total, RobotsReader.ReadBlocked(crawl.Path, Site, urlId: home)!.Matching,
            RobotsReader.ReadBlocked(crawl.Path, Site, urlId: home)!.Rows.Count));
        var partner = RobotsReader.ReadOrigins(crawl.Path).Origins.Single(origin => !origin.IsInternal).Origin;
        var theirs = crawl.Reference.Rows.First(row => ((string)row.Value["url"]!).StartsWith(partner + "/", StringComparison.Ordinal) && row.Value["fetched"] is true).Key;
        Assert.Equal((partner, null), (RobotsTest(crawl.Path, theirs).Origin, RobotsTest(crawl.Path, theirs).Reason));

        // A URL on an origin the crawl never asked robots.txt of names none: there's no file to open.
        var copy = crawl.Copy();
        Change(copy, "UPDATE urls SET url = 'https://nobody-asked.example/page' WHERE id = $id", ("$id", theirs));
        Assert.Null(RobotsTest(copy, theirs).Origin);

        // Why, where no line did: a longer rule on another line, robots.txt unreadable when the URL came up, and a file
        // read again since that refuses nothing. Each is what the origin's list says of the URL.
        string With(string sql, params (string Name, object Value)[] parameters)
        {
            var changed = crawl.Copy();
            Change(changed, sql, [.. parameters, ("$site", Site)]);
            return changed;
        }

        var specific = With("UPDATE robots SET content = $content WHERE origin = $site",
            ("$content", "# The shop\r\nUser-agent: *\r\nDisallow: /private/\r\nDisallow: /private/1\r\nAllow: /private/2$\r\nDisallow: /private/2$\r\n"));
        var failedOnce = With("UPDATE responses SET error = 'robots.txt unreachable: HTTP 503' WHERE url_id = $id", ("$id", blocked[1].Id));
        var rewritten = With("UPDATE robots SET content = 'User-agent: *' || char(10) || 'Disallow: /nothing/' WHERE origin = $site");
        var gone = With("DELETE FROM robots WHERE origin = $site");
        HashSet<(RobotsBlockReason, int?)> seen = [];
        foreach (var path in new[] { specific, failedOnce, rewritten, gone })
        {
            foreach (var row in RobotsReader.ReadBlocked(path, Site, limit: 1_000)!.Rows)
            {
                var test = RobotsTest(path, row.Id);
                Assert.Equal((TestOutcome.Fail, Site, row.Reason, row.Line, row.Rule, row.Reason == RobotsBlockReason.Unreachable && path == failedOnce),
                    (test.Outcome, test.Origin, test.Reason, test.Line, test.Rule, test.Unreachable));
                seen.Add((row.Reason, row.Line));
            }
        }

        Assert.Superset(new HashSet<(RobotsBlockReason, int?)> { (RobotsBlockReason.Rule, 3), (RobotsBlockReason.Rule, 4), (RobotsBlockReason.Unknown, null), (RobotsBlockReason.Unreachable, null) }, seen);

        IndexabilityTest RobotsTest(string path, int id) => UrlInspectorReader.Read(path, id)!.Indexability.Tests.Single(test => test.Test == IndexabilityTestKind.RobotsTxt);
    }

    /// <summary>
    /// The inspector names the sitemaps that list a URL, in the order the crawl found them, with or without a URL summary:
    /// every one while they're few, the first twenty and their number past that.
    /// </summary>
    [Fact]
    public void TheInspectorNamesTheSitemapsThatListAUrl()
    {
        var sitemaps = SitemapListReader.Read(crawl.Path).Sitemaps.ToDictionary(sitemap => sitemap.Id, sitemap => sitemap.Url);
        var listed = 0;
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            foreach (var id in crawl.Reference.Rows.Keys)
            {
                var expected = TestCrawls.Rows(path, $"SELECT printf('%09d', sitemap_id) FROM sitemap_urls WHERE url_id = {id}").Select(int.Parse).Order().ToList();
                var read = UrlInspectorReader.Read(path, id)!.ListedIn;
                Assert.Equal(expected.Count, read.Total);
                Assert.Equal(expected.Select(sitemap => new SitemapRef(sitemap, sitemaps[sitemap])), read.Sitemaps);
                listed += expected.Count;
            }
        }

        Assert.True(listed > GridCrawl.PAGES, $"Only {listed} listings were checked.");

        // Thirty sitemaps more that all list the home page: the first twenty of all that list it are named, and all counted.
        var copy = crawl.Copy();
        var home = UrlGridReader.Lookup(crawl.Path, Site + "/")!.Id;
        var before = UrlInspectorReader.Read(copy, home)!.ListedIn;
        for (var extra = 0; extra < 30; extra++)
        {
            Change(copy, $"INSERT INTO sitemaps (url, status_code, url_count) VALUES ('{Site}/sitemaps/more-{extra}.xml', 200, 1)");
            Change(copy, "INSERT INTO sitemap_urls (sitemap_id, url_id) VALUES ((SELECT MAX(id) FROM sitemaps), $id)", ("$id", home));
        }

        var many = UrlInspectorReader.Read(copy, home)!.ListedIn;
        Assert.Equal((before.Total + 30, UrlInspectorReader.MAX_SITEMAPS_NAMED), (many.Total, many.Sitemaps.Count));
        Assert.Equal(many.Sitemaps.Select(sitemap => sitemap.Id).Order(), many.Sitemaps.Select(sitemap => sitemap.Id));
        Assert.Equal(before.Sitemaps, many.Sitemaps.Take(before.Sitemaps.Count));
        Assert.Equal($"{Site}/sitemaps/more-0.xml", many.Sitemaps[before.Sitemaps.Count].Url);
    }

    /// <summary>
    /// An index says how many sitemaps its file lists, beside how many of them the crawl has; a sitemap of URLs lists
    /// none, one that couldn't be read doesn't say, and neither does a crawl made before the count was kept.
    /// </summary>
    [Fact]
    public void AnIndexSaysHowManySitemapsItsFileLists()
    {
        var list = SitemapListReader.Read(crawl.Path).Sitemaps;
        var (index, pages, extra, empty, missing) = (list[0], list[1], list[2], list[3], list[4]);
        Assert.Equal((4, 4), (index.ListedSitemaps, index.Sitemaps));
        Assert.Equal((0, 0, 0, null), (pages.ListedSitemaps, extra.ListedSitemaps, empty.ListedSitemaps, missing.ListedSitemaps));
        Assert.Equal(SitemapState.Failed, missing.State);

        // The crawl has fewer than the file lists where it didn't go on to read them all.
        var fewer = crawl.Copy();
        Change(fewer, "UPDATE sitemaps SET listed_sitemaps = 5000 WHERE id = $id", ("$id", index.Id));
        var over = SitemapListReader.Read(fewer).Sitemaps[0];
        Assert.Equal((5_000, 4), (over.ListedSitemaps, over.Sitemaps));

        // A file from before the count was kept has no column for it: nothing is said, and nothing fails.
        var earlier = crawl.Copy();
        Change(earlier, "ALTER TABLE sitemaps DROP COLUMN listed_sitemaps");
        var read = SitemapListReader.Read(earlier).Sitemaps;
        Assert.All(read, sitemap => Assert.Null(sitemap.ListedSitemaps));
        Assert.Equal(list.Select(sitemap => (sitemap.Url, sitemap.Sitemaps, sitemap.Entries, sitemap.Urls)), read.Select(sitemap => (sitemap.Url, sitemap.Sitemaps, sitemap.Entries, sitemap.Urls)));

        // A count no file could have reads as not known.
        var crafted = crawl.Copy();
        Change(crafted, "UPDATE sitemaps SET listed_sitemaps = -4 WHERE id = $id", ("$id", index.Id));
        Assert.Null(SitemapListReader.Read(crafted).Sitemaps[0].ListedSitemaps);
    }

    /// <summary>The URL ids one sitemap lists, or every sitemap when none is named, each once.</summary>
    private static HashSet<int> Listed(string path, int? sitemapId) =>
    [
        .. TestCrawls.Rows(path, $"SELECT DISTINCT url_id FROM sitemap_urls{(sitemapId is { } id ? $" WHERE sitemap_id = {id}" : string.Empty)}")
            .Select(row => int.Parse(row, System.Globalization.CultureInfo.InvariantCulture))
    ];

    private object? Row(int id, string column) => crawl.Reference.Rows[id][column];
}
