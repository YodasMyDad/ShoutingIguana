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
        Assert.Equal(($"{Site}/sitemap.xml", null, SitemapSource.RobotsTxt, SitemapState.Read, 200, true, 0, 3, null),
            (index.Url, index.ParentId, index.FoundVia, index.State, index.StatusCode, index.IsIndex, index.Entries, index.Sitemaps, index.Error));
        Assert.Equal([$"{Site}/sitemaps/pages-0.xml", $"{Site}/sitemaps/extra.xml", $"{Site}/sitemaps/missing.xml"], list.Sitemaps.Skip(1).Select(sitemap => sitemap.Url));
        Assert.All(list.Sitemaps.Skip(1), sitemap => Assert.Equal((index.Id, SitemapSource.Index, 0, false), (sitemap.ParentId, sitemap.FoundVia, sitemap.Sitemaps, sitemap.IsIndex)));
        var (pages, extra, missing) = (list.Sitemaps[1], list.Sitemaps[2], list.Sitemaps[3]);
        Assert.Equal((SitemapState.Failed, 404, "HTTP 404", new SitemapUrlCounts(0, 0, 0, 0)), (missing.State, missing.StatusCode, missing.Error, missing.Urls));
        Assert.Equal((SitemapState.Read, 200, null, GridCrawl.PAGES), (pages.State, pages.StatusCode, pages.Error, pages.Entries));
        Assert.Equal((SitemapState.Read, TestCrawls.Count(crawl.Path, $"SELECT url_count FROM sitemaps WHERE id = {extra.Id}")), (extra.State, extra.Entries));
        Assert.True(extra.Entries >= Planted.GetExpectations().Orphans + 3);

        // Each sitemap's counts are of the URLs it lists, as the URL grid judges them; an index's are of every sitemap under it.
        foreach (var sitemap in list.Sitemaps)
        {
            var listed = Listed(crawl.Path, sitemap.Id == index.Id ? null : sitemap.Id);
            Assert.Equal(new SitemapUrlCounts(listed.Count, listed.Count(id => Row(id, "fetched") is true), listed.Count(id => Row(id, "indexable") is true),
                listed.Count(id => Row(id, "indexable") is false)), sitemap.Urls);
        }

        Assert.Equal(pages.Urls.Listed + extra.Urls.Listed, index.Urls.Listed);
        Assert.True(pages.Urls.NonIndexable > 0 && pages.Urls.Indexable > pages.Urls.NonIndexable && extra.Urls.Fetched == extra.Urls.Listed);

        // Every count opens its URLs in the sitemap's grid, with or without a URL summary.
        foreach (var path in new[] { crawl.Path, crawl.Changed })
        {
            var read = SitemapListReader.Read(path);
            Assert.Equal(path == crawl.Path, read.Summary.Available);
            foreach (var sitemap in read.Sitemaps)
            {
                long Opens(params UrlFilter[] filters) => UrlGridReader.QuerySitemap(path, sitemap.Id, new UrlQuery { Filters = filters, Limit = 1 })!.Matching;
                Assert.Equal(sitemap.Urls, new SitemapUrlCounts(Opens(), Opens(Boolean("fetched", true)), Opens(Boolean("indexable", true)), Opens(Boolean("indexable", false))));
            }

            Assert.Equal(list.Sitemaps.Select(sitemap => sitemap.Urls.Listed), read.Sitemaps.Select(sitemap => sitemap.Urls.Listed));
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

        // A URL two sitemaps list counts once under their index.
        var twice = After("INSERT INTO sitemap_urls SELECT 3, url_id FROM sitemap_urls WHERE sitemap_id = 2 ORDER BY url_id LIMIT 7");
        Assert.Equal(index.Urls, twice.Sitemaps[0].Urls);
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
        Assert.Equal([SitemapState.Read, SitemapState.Pending, SitemapState.NotRead, SitemapState.Failed], states.Sitemaps.Select(sitemap => sitemap.State));

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
        var blocked = crawl.Reference.Rows.Where(row => (string?)row.Value["outcome"] == "robotsDisallowed").Select(row => row.Key).Order().ToList();
        Assert.Equal(Planted.GetExpectations().RobotsDisallowed, blocked.Count);
        Assert.True(blocked.Count > 2);

        var origins = RobotsReader.ReadOrigins(crawl.Path);
        Assert.Equal((TestCrawls.Count(crawl.Path, "SELECT COUNT(*) FROM robots"), 0, true, "ShoutingIguana", blocked.Count),
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

        // The partner site's answers 404: no rules, and nothing of it blocked. The list pages.
        var partner = Assert.Single(origins.Origins, origin => !origin.IsInternal);
        Assert.Equal((RobotsState.Missing, 404, null, 0), (partner.State, partner.StatusCode, partner.Bytes, partner.Blocked));
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
        Assert.Empty(RobotsReader.ReadBlocked(crawl.Path, partner.Origin)!.Rows);

        Assert.Null(RobotsReader.ReadFile(crawl.Path, "https://elsewhere.example"));
        Assert.Null(RobotsReader.ReadBlocked(crawl.Path, "https://elsewhere.example"));

        // The same without a URL summary: nothing here reads it.
        Assert.Equal(all.Rows, RobotsReader.ReadBlocked(crawl.Changed, Site)!.Rows);
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
        Assert.All(RobotsReader.ReadBlocked(unreachable, Site, limit: 1_000)!.Rows, row => Assert.Equal((RobotsBlockReason.Unreachable, "HTTP 503"), (row.Reason, row.Error)));
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

    /// <summary>The URL ids one sitemap lists, or every sitemap when none is named, each once.</summary>
    private static HashSet<int> Listed(string path, int? sitemapId) =>
    [
        .. TestCrawls.Rows(path, $"SELECT DISTINCT url_id FROM sitemap_urls{(sitemapId is { } id ? $" WHERE sitemap_id = {id}" : string.Empty)}")
            .Select(row => int.Parse(row, System.Globalization.CultureInfo.InvariantCulture))
    ];

    private object? Row(int id, string column) => crawl.Reference.Rows[id][column];
}
