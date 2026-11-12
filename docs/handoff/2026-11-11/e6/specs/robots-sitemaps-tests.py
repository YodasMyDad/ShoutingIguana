T = 'tests/ShoutingIguana.Engine.Tests/'

EDITS = [
(T + 'Storage/RobotsAndSitemapsReaderTests.cs',
'''    /// <summary>The URL ids one sitemap lists, or every sitemap when none is named, each once.</summary>
    private static HashSet<int> Listed(string path, int? sitemapId) =>
''',
'''    /// <summary>
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
        Assert.Equal([Site, "https://m.example", "https://b.example", "https://z.example"], blocking.Select(origin => origin.Origin));

        // With nothing asked, every origin matches, and the counts are the crawl's.
        Assert.Equal((8L, new RobotsOriginCounts(8, 4, 4)), (all.Matching, all.Counts));

        // Those that blocked something, and those that blocked nothing: the list's order, and what's left of it.
        RobotsOriginPage Read(RobotsOriginQuery query) => RobotsReader.ReadOrigins(copy, query);
        var blocked = Read(new RobotsOriginQuery { Blocked = true });
        Assert.Equal(blocking, blocked.Origins);
        Assert.Equal((8L, 4L, all.Blocked, new RobotsOriginCounts(8, 4, 4)), (blocked.Total, blocked.Matching, blocked.Blocked, blocked.Counts));
        var clear = Read(new RobotsOriginQuery { Blocked = false });
        Assert.Equal(all.Origins.Except(blocking), clear.Origins);
        Assert.Equal((4L, new RobotsOriginCounts(8, 4, 4)), (clear.Matching, clear.Counts));

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
        Assert.Equal((["https://b.example"], 3L, 1), (paged.Origins.Select(origin => origin.Origin), paged.Matching, paged.Offset));
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
                Assert.Equal((blocked.Count, 1L, [row]), (one.Total, one.Matching, one.Rows));
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
            ("$content", "# The shop\\r\\nUser-agent: *\\r\\nDisallow: /private/\\r\\nDisallow: /private/1\\r\\nAllow: /private/2$\\r\\nDisallow: /private/2$\\r\\n"));
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
                Assert.Equal((expected.Count, expected.Select(sitemap => new SitemapRef(sitemap, sitemaps[sitemap]))), (read.Total, read.Sitemaps));
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
        var (index, pages, extra, missing) = (list[0], list[1], list[2], list[3]);
        Assert.Equal((3, 3), (index.ListedSitemaps, index.Sitemaps));
        Assert.Equal((0, 0, null), (pages.ListedSitemaps, extra.ListedSitemaps, missing.ListedSitemaps));
        Assert.Equal(SitemapState.Failed, missing.State);

        // The crawl has fewer than the file lists where it didn't go on to read them all.
        var fewer = crawl.Copy();
        Change(fewer, "UPDATE sitemaps SET listed_sitemaps = 5000 WHERE id = $id", ("$id", index.Id));
        var over = SitemapListReader.Read(fewer).Sitemaps[0];
        Assert.Equal((5_000, 3), (over.ListedSitemaps, over.Sitemaps));

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
'''),
(T + 'Sitemaps/SitemapDiscoveryTests.cs',
'''        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(3, sitemap.UrlCount);
        Assert.NotNull(sitemap.Error);
        Assert.Equal(3, Count(path, "SELECT COUNT(*) FROM sitemap_urls"));
    }
''',
'''        var sitemap = Assert.Single(Sitemaps(path));
        Assert.Equal(3, sitemap.UrlCount);
        Assert.NotNull(sitemap.Error);
        Assert.Equal(3, Count(path, "SELECT COUNT(*) FROM sitemap_urls"));

        // Its body was read as far as it went, and listed no sitemap: that much is known of it.
        Assert.Equal(0, Assert.Single(SitemapListReader.Read(path).Sitemaps).ListedSitemaps);
    }
'''),
(T + 'Sitemaps/SitemapDiscoveryTests.cs',
'''        // An index of more sitemaps than the crawler reads: it and all but the last of them are read.
        Serve("/robots.txt", "text/plain", $"User-agent: *\\nDisallow:\\nSitemap: {_server.Url("/index.xml")}");
        Serve("/index.xml", "application/xml", $"<sitemapindex xmlns=\\"http://www.sitemaps.org/schemas/sitemap/0.9\\">{string.Concat(
            Enumerable.Range(0, SitemapDiscoverer.MAX_SITEMAPS).Select(child => $"<sitemap><loc>{_server.Url($"/part-{child}.xml")}</loc></sitemap>"))}</sitemapindex>");
''',
'''        // An index of more sitemaps than the crawler reads, by forty, one of them listed twice and one that's no URL: it
        // and the first of them are read, up to the limit.
        const int OVER = 40;
        Serve("/robots.txt", "text/plain", $"User-agent: *\\nDisallow:\\nSitemap: {_server.Url("/index.xml")}");
        Serve("/index.xml", "application/xml", $"<sitemapindex xmlns=\\"http://www.sitemaps.org/schemas/sitemap/0.9\\">{string.Concat(
            Enumerable.Range(0, SitemapDiscoverer.MAX_SITEMAPS + OVER).Select(child => $"<sitemap><loc>{_server.Url($"/part-{child}.xml")}</loc></sitemap>"))}<sitemap><loc>{
                _server.Url("/part-0.xml")}</loc></sitemap><sitemap><loc>not a URL</loc></sitemap></sitemapindex>");
'''),
(T + 'Sitemaps/SitemapDiscoveryTests.cs',
'''        Assert.Equal($"{SitemapDiscoverer.NOT_READ_ERROR}: the crawler reads at most 1,000 sitemaps", notRead.Error);
        Assert.Null(notRead.StatusCode);
        Assert.False(notRead.IsProbe);
        Assert.Equal(1, observer.Count("sitemaps-not-read"));
    }

    /// <remarks>
''',
'''        Assert.Equal($"{SitemapDiscoverer.NOT_READ_ERROR}: the crawler reads at most 1,000 sitemaps", notRead.Error);
        Assert.Null(notRead.StatusCode);
        Assert.False(notRead.IsProbe);
        Assert.Equal(1, observer.Count("sitemaps-not-read"));

        // The index says how many sitemaps its file lists, every entry of it, though the crawl stopped at its limit: it
        // has the 999 it read and the one it recorded as left unread. A sitemap read lists none, and the one left unread
        // doesn't say.
        var list = SitemapListReader.Read(path).Sitemaps;
        Assert.Equal((SitemapDiscoverer.MAX_SITEMAPS + OVER + 2, SitemapDiscoverer.MAX_SITEMAPS, true), (list[0].ListedSitemaps, list[0].Sitemaps, list[0].IsIndex));
        Assert.All(list.Skip(1).Take(SitemapDiscoverer.MAX_SITEMAPS - 1), sitemap => Assert.Equal((SitemapState.Read, 0), (sitemap.State, sitemap.ListedSitemaps)));
        Assert.Equal((SitemapState.NotRead, null), (list[^1].State, list[^1].ListedSitemaps));
    }

    /// <remarks>
'''),
]
