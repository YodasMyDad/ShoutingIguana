using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The URL summary of a small site whose every link, redirect and type is known, so each number can be stated exactly.
/// The end-to-end crawls of the synthetic site check it against the crawl's tables and the checks at scale.
/// </summary>
public sealed class UrlSummaryTests : IDisposable
{
    private readonly CrawlFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task EachUrlHasWhatTheAnalysisJudgedAndWhatTheCrawlFound()
    {
        await using var site = await LoopbackServer.StartAsync(Serve);
        var path = _files.NewPath();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 }, path)).Status);
        Assert.False(UrlSummaryReader.IsAvailable(path));
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        Assert.True(UrlSummaryReader.IsAvailable(path));
        Assert.True(IssueReader.ReadAnalysis(path)!.HasUrlSummary);
        var ids = TestCrawls.Rows(path, "SELECT url, id FROM urls").Select(row => row.Split(" | "))
            .ToDictionary(row => row[0], row => int.Parse(row[1], CultureInfo.InvariantCulture));
        var rows = UrlSummaryReader.ReadAll(path).ToList();
        Assert.Equal(ids.Values.Order(), rows.Select(row => row.UrlId));
        var byPath = rows.Where(row => ids.ContainsValue(row.UrlId))
            .ToDictionary(row => ids.Single(pair => pair.Value == row.UrlId).Key);
        UrlSummary Row(string pathAndQuery) => byPath[site.Url(pathAndQuery).AbsoluteUri];

        // Inlinks: each link counted, nofollow by its rel or by its page's robots directives, never a page's own links.
        var home = Row("/");
        var a = Row("/a");
        var b = Row("/b");
        var c = Row("/c");
        Assert.Equal((3, 1, 2, 2), (a.Inlinks, a.FollowedInlinks, a.NofollowInlinks, a.LinkingPages));
        Assert.Equal((2, 1, 1, 2), (b.Inlinks, b.FollowedInlinks, b.NofollowInlinks, b.LinkingPages));
        Assert.Equal((1, 0, 1, 1), (c.Inlinks, c.FollowedInlinks, c.NofollowInlinks, c.LinkingPages));
        Assert.Equal((1, 1, 0, 1), (home.Inlinks, home.FollowedInlinks, home.NofollowInlinks, home.LinkingPages));
        Assert.Equal((0, 0), (Row("/chain").Inlinks, Row("/img.png").Inlinks));
        Assert.Equal(((int?)3, (int?)null), (a.Outlinks, Row("/img.png").Outlinks));

        // A URL the crawl never fetched (a nofollow page's link) is unknown, not a failure.
        Assert.Equal(((int?)null, (bool?)null, (IndexabilityReason?)null, c.UrlId, (int?)null, (int?)null, (int?)null, ResourceType.Page),
            (c.StatusCode, c.IsIndexable, c.IndexabilityReason, c.FinalUrlId, c.FinalStatusCode, c.ResponseMs, c.Outlinks, c.ResourceType));

        // Redirects, an instant meta refresh among them, end where they lead.
        Assert.Equal(((int?)301, a.UrlId, (int?)200, (bool?)false, (IndexabilityReason?)IndexabilityReason.Redirected),
            (Row("/old").StatusCode, Row("/old").FinalUrlId, Row("/old").FinalStatusCode, Row("/old").IsIndexable, Row("/old").IndexabilityReason));
        Assert.Equal(((int?)302, a.UrlId, (int?)200), (Row("/chain").StatusCode, Row("/chain").FinalUrlId, Row("/chain").FinalStatusCode));
        var refresh = Row("/refresh");
        Assert.Equal(((int?)200, b.UrlId, (int?)200, (bool?)false, (IndexabilityReason?)IndexabilityReason.Redirected),
            (refresh.StatusCode, refresh.FinalUrlId, refresh.FinalStatusCode, refresh.IsIndexable, refresh.IndexabilityReason));

        // Indexable or not, and why.
        Assert.Equal(((bool?)true, (IndexabilityReason?)IndexabilityReason.None, home.UrlId, (int?)200), (home.IsIndexable, home.IndexabilityReason, home.FinalUrlId, home.FinalStatusCode));
        Assert.Equal(((bool?)false, (IndexabilityReason?)IndexabilityReason.Noindex), (Row("/noindex").IsIndexable, Row("/noindex").IndexabilityReason));
        Assert.Equal(((int?)404, (bool?)false, (IndexabilityReason?)IndexabilityReason.ClientError, (int?)404), (Row("/missing").StatusCode, Row("/missing").IsIndexable,
            Row("/missing").IndexabilityReason, Row("/missing").FinalStatusCode));

        // robots.txt refused it: no request, so no status, size or time.
        var refused = Row("/private");
        Assert.Equal(((int?)null, (bool?)false, (IndexabilityReason?)IndexabilityReason.BlockedByRobots, (long?)null, (int?)null),
            (refused.StatusCode, refused.IsIndexable, refused.IndexabilityReason, refused.SizeBytes, refused.ResponseMs));

        // Types: by content type once fetched successfully, otherwise by the extension: a missing image is an image, even
        // when its 404 page is HTML.
        Assert.Equal([ResourceType.Page, ResourceType.Pdf, ResourceType.Other, ResourceType.Image, ResourceType.Stylesheet, ResourceType.Script],
            new[] { "/", "/doc.pdf", "/data.json", "/img.png", "/style.css", "/app.js" }.Select(url => Row(url).ResourceType));
        Assert.Equal((ResourceType.Image, (int?)404), (Row("/missing.jpg").ResourceType, Row("/missing.jpg").StatusCode));
        Assert.Equal(((bool?)true, (IndexabilityReason?)IndexabilityReason.None), (Row("/doc.pdf").IsIndexable, Row("/doc.pdf").IndexabilityReason));

        // The copies of the crawl's own numbers, and the sitemap.
        Assert.Equal((true, (int?)0, (int?)1), (home.IsInternal, home.Depth, a.Depth));
        Assert.True(a is { ResponseMs: not null, SizeBytes: > 0, WordCount: > 0 });
        Assert.Equal([true, true, true, false], new[] { "/", "/a", "/orphan", "/b" }.Select(url => Row(url).InSitemap));
        Assert.Equal((DiscoverySource.Seed, DiscoverySource.Link, DiscoverySource.Sitemap), (home.Source, b.Source, Row("/orphan").Source));

        // Only a sitemap leads to the orphan: no click depth, rather than one a depth filter would count.
        Assert.Null(Row("/orphan").Depth);
        Assert.Equal((long)CrawlDepth.UNLINKED, TestCrawls.Count(path, $"SELECT depth FROM urls WHERE id = {Row("/orphan").UrlId}"));

        // The crawler's probe of the site's https:// version, which nothing on the site leads to, gets no answer from this
        // plain-HTTP server. The summary says so, marked as the probe it is; the stats, like the checks, leave it out.
        var probe = Assert.Single(rows, row => row.Source == DiscoverySource.SiteVersion);
        Assert.Equal((true, (int?)0, (IndexabilityReason?)IndexabilityReason.Unreachable), (probe.IsInternal, probe.Depth, probe.IndexabilityReason));
        var stats = CrawlStatsReader.Read(path);
        Assert.False(stats.NonIndexableReasons.ContainsKey(IndexabilityReason.Unreachable));
        Assert.Equal(stats.IndexablePageCount, rows.Count(row => row is { IsInternal: true, IsIndexable: true, ResourceType: ResourceType.Page }));

        // Issues by severity: the orphan, only a sitemap leads to, is one warning among them.
        var orphanIssues = IssueReader.ReadAll(path).Where(row => row.Url == site.Url("/orphan").AbsoluteUri).Select(row => IssueCatalog.Find(row.Issue)!).ToList();
        Assert.Contains(IssueCatalog.ORPHAN_PAGE, orphanIssues);
        var orphan = Row("/orphan");
        Assert.Equal((orphanIssues.Count, orphanIssues.Count(issue => issue.Severity == IssueSeverity.Error),
                orphanIssues.Count(issue => issue.Severity == IssueSeverity.Warning), orphanIssues.Count(issue => issue.Severity == IssueSeverity.Notice)),
            (orphan.Issues, orphan.Errors, orphan.Warnings, orphan.Notices));
        Assert.All(rows, row => Assert.Equal(row.Issues, row.Errors + row.Warnings + row.Notices));
    }

    /// <summary>The indexes the grids sort and filter on are there, and a sort walks its index rather than sorting.</summary>
    [Fact]
    public async Task TheGridsSortsReadAnIndex()
    {
        await using var site = await LoopbackServer.StartAsync(Serve);
        var path = _files.NewPath();
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 }, path);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        (string Column, string Index)[] sorted =
        [
            ("status_code", "status"), ("indexability_reason", "indexability"), ("resource_type", "type"), ("depth", "depth"),
            ("linking_pages", "linking_pages"), ("issues", "issues"), ("response_ms", "response_ms"), ("size_bytes", "size"), ("word_count", "words")
        ];
        Assert.Equal(sorted.Select(sort => "ix_url_summary_" + sort.Index).Order(StringComparer.Ordinal),
            TestCrawls.Rows(path, "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'url_summary'"));
        Assert.All(sorted, sort =>
        {
            var plan = string.Join(" ; ", TestCrawls.Rows(path, $"EXPLAIN QUERY PLAN SELECT url_id FROM url_summary ORDER BY {sort.Column} DESC, url_id DESC LIMIT 100"));
            Assert.Contains("ix_url_summary_" + sort.Index, plan);
            Assert.DoesNotContain("TEMP B-TREE", plan);
        });

        // Analysed again, the summary is rebuilt, not added to.
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"), TestCrawls.Count(path, "SELECT COUNT(*) FROM url_summary"));
    }

    /// <summary>
    /// A stopped crawl is analysed, then continued: from the moment it's continued the summary describes an earlier crawl,
    /// so it isn't read, while the crawl runs, once it's stopped again, and until it's analysed again. Other thresholds
    /// don't change what the summary describes, so it is read behind the out-of-date notice.
    /// </summary>
    [Fact]
    public async Task ASummaryOfAnEarlierStateOfTheCrawlIsNeverRead()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var site = await LoopbackServer.StartAsync(async context =>
        {
            if (context.Request.Path.Value == "/held")
            {
                await held.Task.WaitAsync(context.RequestAborted);
            }

            await Page(context, context.Request.Path.Value == "/" ? """<a href="/a">A</a> <a href="/held">Held</a>""" : "A page");
        });
        var path = _files.NewPath();
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };
        var a = $"SELECT COUNT(*) FROM responses r JOIN urls u ON u.id = r.url_id WHERE u.url = '{site.Url("/a").AbsoluteUri}'";
        await using (var job = TestCrawls.Start(options, path))
        {
            await TestCrawls.WaitUntilAsync(path, a);
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync()).Status);
        }

        var analyzer = new CrawlAnalyzer(NullLoggerFactory.Instance);
        await analyzer.AnalyzeAsync(path);
        Assert.True(UrlSummaryReader.IsAvailable(path));
        await analyzer.AnalyzeAsync(path, new AnalysisSettings { TitleMaxLength = 70 });
        Assert.Equal([OutOfDateReason.SettingsChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
        Assert.True(UrlSummaryReader.IsAvailable(path));
        var analysed = IssueReader.ReadAnalysis(path)!.CrawlUpdatedAt!.Value.ToUnixTimeMilliseconds();

        await using (var resumed = TestCrawls.Start(options, path, resume: true))
        {
            await TestCrawls.WaitUntilAsync(path, $"SELECT COUNT(*) FROM crawl WHERE updated_at <> {analysed}");
            Assert.False(UrlSummaryReader.IsAvailable(path));
            Assert.Throws<InvalidOperationException>(() => UrlSummaryReader.ReadAll(path).ToList());
            Assert.Equal(CrawlStatus.Stopped, (await resumed.StopAsync()).Status);
        }

        Assert.False(UrlSummaryReader.IsAvailable(path));
        held.SetResult();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(options, path, resume: true)).Status);
        Assert.False(UrlSummaryReader.IsAvailable(path));

        await analyzer.AnalyzeAsync(path);
        Assert.True(UrlSummaryReader.IsAvailable(path));
        Assert.Equal(TestCrawls.Count(path, "SELECT COUNT(*) FROM urls"), UrlSummaryReader.ReadAll(path).Count());
    }

    /// <summary>
    /// A crawl kept to a folder only status-checks the site's pages outside it: their meta robots, canonical and links are
    /// never read, so the summary can't call them indexable, nor say they have no links. What their response proves (an
    /// X-Robots-Tag noindex, an error, a redirect) it does say, and the stats count the indexable pages it does.
    /// </summary>
    [Fact]
    public async Task APageOnlyStatusCheckedIsNeitherIndexableNorNot()
    {
        await using var site = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/blog/" => Page(context, """
                <a href="/blog/post">Post</a> <a href="/shop/hidden">Hidden</a> <a href="/shop/header-noindex">Header noindex</a>
                <a href="/shop/fine">Fine</a> <a href="/shop/gone">Gone</a> <a href="/shop/moved">Moved</a>
                """),
            "/blog/post" => Page(context, "A post"),
            "/shop/hidden" => Page(context, """<a href="/shop/fine">Fine</a>""", head: """<meta name="robots" content="noindex">"""),
            "/shop/header-noindex" => NoindexByHeader(context),
            "/shop/fine" => Page(context, """<a href="/blog/post">Post</a>"""),
            "/shop/moved" => Redirect(context, StatusCodes.Status301MovedPermanently, "/blog/post"),
            _ => NotFound(context)
        });
        var path = _files.NewPath();
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.Url("/blog/").AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false }, path);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);

        var ids = TestCrawls.Rows(path, "SELECT url, id FROM urls").Select(row => row.Split(" | "))
            .ToDictionary(row => row[0], row => int.Parse(row[1], CultureInfo.InvariantCulture));
        var rows = UrlSummaryReader.ReadAll(path).ToDictionary(row => row.UrlId);
        UrlSummary Row(string pathAndQuery) => rows[ids[site.Url(pathAndQuery).AbsoluteUri]];
        (int?, bool?, IndexabilityReason?, int?) Judged(string pathAndQuery) =>
            (Row(pathAndQuery).StatusCode, Row(pathAndQuery).IsIndexable, Row(pathAndQuery).IndexabilityReason, Row(pathAndQuery).Outlinks);

        Assert.Equal(((int?)200, (bool?)true, (IndexabilityReason?)IndexabilityReason.None, (int?)0), Judged("/blog/post"));
        Assert.Equal(((int?)200, (bool?)null, (IndexabilityReason?)null, (int?)null), Judged("/shop/hidden"));
        Assert.Equal(((int?)200, (bool?)null, (IndexabilityReason?)null, (int?)null), Judged("/shop/fine"));
        Assert.Equal(((int?)200, (bool?)false, (IndexabilityReason?)IndexabilityReason.Noindex, (int?)null), Judged("/shop/header-noindex"));
        Assert.Equal(((int?)404, (bool?)false, (IndexabilityReason?)IndexabilityReason.ClientError, (int?)null), Judged("/shop/gone"));
        Assert.Equal(((int?)301, (bool?)false, (IndexabilityReason?)IndexabilityReason.Redirected, (int?)null), Judged("/shop/moved"));
        Assert.Equal(ResourceType.Page, Row("/shop/hidden").ResourceType);

        // Sitemaps weren't read, so no URL is known to be missing from them.
        Assert.All(rows.Values, row => Assert.Null(row.InSitemap));

        // Every page called indexable is one the analysis audited, and the stats count those, and only those.
        var indexable = rows.Values.Where(row => row is { IsInternal: true, IsIndexable: true, ResourceType: ResourceType.Page }).Select(row => row.UrlId).Order().ToList();
        Assert.Equal(new[] { "/blog/", "/blog/post" }.Select(url => ids[site.Url(url).AbsoluteUri]).Order(), indexable);
        Assert.Equal(indexable.Count, CrawlStatsReader.Read(path).IndexablePageCount);
    }

    /// <summary>
    /// The summary's columns change with the analysis version, so one another version of the engine stored is never read:
    /// the crawl needs analysing, rather than a read that fails on a column that isn't there.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task ASummaryFromAnotherVersionOfTheEngineIsNotRead(int versionsAway)
    {
        await using var site = await LoopbackServer.StartAsync(Serve);
        var path = _files.NewPath();
        await TestCrawls.RunAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 }, path);
        await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(path);
        Change(path, $"""
            UPDATE analysis SET analysis_version = {AnalysisFreshness.VERSION + versionsAway};
            DROP INDEX ix_url_summary_words;
            ALTER TABLE url_summary DROP COLUMN word_count;
            """);

        Assert.False(IssueReader.ReadAnalysis(path)!.HasUrlSummary);
        Assert.False(UrlSummaryReader.IsAvailable(path));
        Assert.Throws<InvalidOperationException>(() => UrlSummaryReader.ReadAll(path).ToList());
        Assert.Equal([OutOfDateReason.EngineChanged], CrawlSummaryReader.Read(path).OutOfDate(new AnalysisSettings()));
    }

    /// <summary>Changes the file as another engine would have left it.</summary>
    private static void Change(string path, string sql)
    {
        var connection = CrawlDatabase.OpenWriter(path, allowNew: false);
        try
        {
            CrawlDatabase.Execute(connection, sql);
        }
        finally
        {
            CrawlDatabase.CloseWriter(connection, NullLogger.Instance);
        }
    }

    private static Task Serve(HttpContext context)
    {
        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        return context.Request.Path.Value switch
        {
            "/robots.txt" => Write(context, "text/plain", $"User-agent: *\nDisallow: /private\nSitemap: {origin}/sitemap.xml"),
            "/sitemap.xml" => Write(context, "application/xml",
                $"<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{origin}/</loc></url><url><loc>{origin}/a</loc></url><url><loc>{origin}/orphan</loc></url></urlset>"),
            "/" => Page(context, """
                <a href="/a">A</a> <a href="/a" rel="nofollow">A again</a> <a href="/b">B</a> <a href="#top">Top</a> <a href="/old">Old</a>
                <a href="/missing">Missing</a> <a href="/private">Private</a> <a href="/refresh">Refresh</a> <a href="/noindex">Noindex</a>
                <a href="/doc.pdf">PDF</a> <a href="/data.json">Data</a> <img src="/img.png" alt="A picture" width="10" height="10">
                <img src="/missing.jpg" alt="A picture that has gone" width="10" height="10">
                """, head: """<link rel="stylesheet" href="/style.css"><script src="/app.js"></script>"""),
            "/a" => Page(context, """<a href="/b" rel="nofollow">B</a> <a href="/a">A</a> <a href="/">Home</a>"""),
            "/b" => Page(context, """<a href="/a">A</a> <a href="/c">C</a>""", head: """<meta name="robots" content="nofollow">"""),
            "/c" => Page(context, "C"),
            "/noindex" => Page(context, "Hidden", head: """<meta name="robots" content="noindex">"""),
            "/refresh" => Page(context, "Moving on", head: """<meta http-equiv="refresh" content="0; url=/b">"""),
            "/orphan" => Page(context, "Only in the sitemap"),
            "/old" => Redirect(context, StatusCodes.Status301MovedPermanently, "/chain"),
            "/chain" => Redirect(context, StatusCodes.Status302Found, "/a"),
            "/doc.pdf" => Write(context, "application/pdf", "%PDF-1.4"),
            "/data.json" => Write(context, "application/json", "{}"),
            "/img.png" => Write(context, "image/png", "png"),
            "/style.css" => Write(context, "text/css", "body {}"),
            "/app.js" => Write(context, "application/javascript", "let a;"),
            _ => NotFound(context)
        };
    }

    private static Task Page(HttpContext context, string body, string head = "") => Write(context, "text/html; charset=utf-8", $"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>{context.Request.Path} on the test site</title>{head}</head>
        <body><main><h1>{context.Request.Path}</h1><p>{body}</p></main></body></html>
        """);

    private static Task NoindexByHeader(HttpContext context)
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex";
        return Page(context, "Kept out of search by its header");
    }

    private static Task Redirect(HttpContext context, int status, string location)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Write(context, "text/html; charset=utf-8", "<!doctype html><title>Not found</title>");
    }

    private static Task Write(HttpContext context, string contentType, string body)
    {
        context.Response.ContentType = contentType;
        return context.Response.WriteAsync(body);
    }
}
