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
        Assert.Equal((3, 0), (a.Outlinks, Row("/img.png").Outlinks));

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

        // Types: by content type once fetched.
        Assert.Equal([ResourceType.Page, ResourceType.Pdf, ResourceType.Other, ResourceType.Image, ResourceType.Stylesheet, ResourceType.Script],
            new[] { "/", "/doc.pdf", "/data.json", "/img.png", "/style.css", "/app.js" }.Select(url => Row(url).ResourceType));
        Assert.Equal(((bool?)true, (IndexabilityReason?)IndexabilityReason.None), (Row("/doc.pdf").IsIndexable, Row("/doc.pdf").IndexabilityReason));

        // The copies of the crawl's own numbers, and the sitemap.
        Assert.Equal((true, 0, 1), (home.IsInternal, home.Depth, a.Depth));
        Assert.True(a is { ResponseMs: not null, SizeBytes: > 0, WordCount: > 0 });
        Assert.Equal([true, true, true, false], new[] { "/", "/a", "/orphan", "/b" }.Select(url => Row(url).InSitemap));
        Assert.Equal(CrawlDepth.UNLINKED, Row("/orphan").Depth);

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
