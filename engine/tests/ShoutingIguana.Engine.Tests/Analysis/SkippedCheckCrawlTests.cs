using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;
using ShoutingIguana.TestSite;
using ShoutingIguana.TestSite.Models;

namespace ShoutingIguana.Engine.Tests.Analysis;

/// <summary>
/// Crawls that can't support a check, analysed end to end: the check is left out of the results, a note says why, and
/// nothing is stored for it, so an export can't show rows for a check the results say didn't run.
/// </summary>
public sealed class SkippedCheckCrawlTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"si-test-{Guid.NewGuid():N}.sicrawl");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>The synthetic site plants every one of these checks, so a check that still ran would store rows.</summary>
    [Fact]
    public async Task ChecksOfWhatTheCrawlDidntFetchAreSkipped()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        var options = new CrawlOptions
        {
            StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8, CheckExternalLinks = false, CheckResources = false, DiscoverSitemaps = false
        };

        var analysis = await CrawlAndAnalyseAsync(options);

        IssueDefinition[] notRun =
        [
            IssueCatalog.BROKEN_EXTERNAL_LINKS, IssueCatalog.IMAGE_BROKEN, IssueCatalog.IMAGE_TOO_LARGE, IssueCatalog.ORPHAN_PAGE,
            IssueCatalog.NOT_IN_SITEMAP, IssueCatalog.SITEMAP_ERROR, IssueCatalog.SITEMAP_MISSING, IssueCatalog.SITEMAP_NON_INDEXABLE,
            IssueCatalog.SITEMAP_URL_OTHER_HOST
        ];
        Assert.True(analysis.IsPartial);
        Assert.Equal(
        [
            "Sitemaps weren't read in this crawl, so orphan pages and the sitemap checks weren't run.",
            "Links to other sites weren't checked in this crawl, so broken external links weren't looked for.",
            "Images weren't fetched in this crawl, so broken and oversized images weren't looked for."
        ], analysis.Notes);
        Assert.Equal(IssueCatalog.All.Except(notRun), analysis.Issues.Select(issue => issue.Issue));
        Assert.DoesNotContain(IssueReader.ReadAll(_path), row => notRun.Any(issue => issue.Id == row.Issue));
    }

    /// <summary>
    /// The site's only sitemap breaks after its first entry, an unlinked page. That page really is in the sitemap, but
    /// with no sitemap read in full the orphan check is skipped, so it isn't stored as an orphan either.
    /// </summary>
    [Fact]
    public async Task ASkippedOrphanCheckStoresNoOrphans()
    {
        await using var site = await LoopbackServer.StartAsync(context => context.Request.Path.Value switch
        {
            "/robots.txt" => Write(context, "text/plain", "User-agent: *\nAllow: /\nSitemap: /sitemap.xml"),
            "/sitemap.xml" => Write(context, "application/xml",
                "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>/unlinked</loc></url><url><loc>/a?x=1&y=2</loc></url></urlset>"
                    .Replace("<loc>/", $"<loc>{context.Request.Scheme}://{context.Request.Host}/", StringComparison.Ordinal)),
            _ => Write(context, "text/html; charset=utf-8", "<!doctype html><html lang=\"en\"><head><title>A page</title></head><body><p>Text</p></body></html>")
        });

        var analysis = await CrawlAndAnalyseAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 });

        Assert.Contains("No sitemap could be read, so orphan pages and pages missing from the sitemaps weren't checked.", analysis.Notes);
        Assert.False(IssueReader.ReadCounts(_path).ContainsKey(IssueCatalog.ORPHAN_PAGE.Id));
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.ORPHAN_PAGE.Id));
        Assert.Equal(1, CrawlStatsReader.Read(_path).SitemapUrlCount);
    }

    /// <summary>
    /// A shop that rate-limits the crawler on its product pages, or puts them behind a bot challenge, leaves their links
    /// unknown, so the collection only they link to would look orphaned. With the refused pages 1 in 20 or more of those the
    /// crawl tried to read, the orphan check is skipped, saying why; one refusal among 22 pages doesn't stop it, and the
    /// collection is then reported. A page the site only ever answered 429 is asked again at the end, at the pace the site
    /// allows, and stored as not checked: it's counted as refused all the same, but isn't internal-blocked, which is for
    /// a firewall's refusals.
    /// </summary>
    /// <param name="others">Pages besides the products that the home page links to, which answer.</param>
    /// <param name="challenge">The products answer with a bot challenge, not 429.</param>
    [Theory]
    [InlineData(2, 0, false, true)]
    [InlineData(1, 19, false, false)]
    [InlineData(2, 0, true, true)]
    public async Task OrphansArentCheckedWhenTheSiteRefusedManyOfThePagesTheCrawlTriedToRead(int refused, int others, bool challenge, bool skipped)
    {
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            var path = context.Request.Path.Value!;
            if (path.StartsWith("/products/", StringComparison.Ordinal))
            {
                if (challenge)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.Headers["cf-mitigated"] = "challenge";
                    return Write(context, "text/html; charset=utf-8", "<!doctype html><title>Just a moment...</title>");
                }

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return Write(context, "text/html; charset=utf-8", "<!doctype html><title>Too many requests</title>");
            }

            return path switch
            {
                "/robots.txt" => Write(context, "text/plain", $"User-agent: *\nAllow: /\nSitemap: {origin}/sitemap.xml"),
                "/sitemap.xml" => Write(context, "application/xml",
                    $"<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{origin}/</loc></url><url><loc>{origin}/collection</loc></url></urlset>"),
                "/" => Write(context, "text/html; charset=utf-8", Page("Home",
                    string.Concat(Enumerable.Range(1, refused).Select(product => $"<a href=\"/products/{product}\">Product {product}</a>")) +
                    string.Concat(Enumerable.Range(1, others).Select(page => $"<a href=\"/pages/{page}\">Page {page}</a>")))),
                _ => Write(context, "text/html; charset=utf-8", Page(path, "<a href=\"/\">Home</a>"))
            };
        });

        // The crawler waits as long as the site asks, so the clock is a fake one, moved on a second at a time.
        var analysis = await CrawlOnAFakeClockAndAnalyseAsync(new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0 });

        // Read: the home page, the collection (from the sitemap) and the others.
        var reason = $"{refused} of the {refused + others + 2} pages the crawl tried to read refused it or didn't answer, and pages linked only from them would look orphaned.";
        if (skipped)
        {
            Assert.Equal(
                [
                    new SkippedCheck(IssueCatalog.ORPHAN_PAGE.Id, SkippedCheckReasons.PAGES_UNREAD, reason),
                    new SkippedCheck(IssueCatalog.CANONICAL_UNLINKED.Id, SkippedCheckReasons.PAGES_UNREAD, reason)
                ], analysis.Skipped);
            Assert.Contains($"Orphan pages weren't checked: {reason}", analysis.Notes);
            Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.ORPHAN_PAGE.Id));
        }
        else
        {
            Assert.Empty(analysis.Skipped);
            Assert.Equal([site.BaseUri + "collection"], IssueReader.ReadAll(_path, IssueCatalog.ORPHAN_PAGE.Id).Select(row => row.Url));
        }

        var blocked = analysis.Issues.Single(issue => issue.Issue == IssueCatalog.INTERNAL_BLOCKED).Count;
        if (challenge)
        {
            Assert.Equal(refused, blocked);
        }
        else
        {
            Assert.Equal(0, blocked);
            Assert.Contains(refused == 1
                ? "1 URL wasn't checked because its site kept asking the crawler to slow down (429 Too Many Requests), so links to it aren't judged. Check the site again later, or at the Polite speed."
                : $"{refused} URLs weren't checked because their sites kept asking the crawler to slow down (429 Too Many Requests), so links to them aren't judged. Check the site again later, or at the Polite speed.",
                analysis.Notes);
        }

        static string Page(string title, string body) =>
            $"<!doctype html><html lang=\"en\"><head><title>{title}</title></head><body><p>Text</p>{body}</body></html>";
    }

    /// <summary>
    /// A crawl told to ignore robots.txt asks for every URL, so none is refused, and the check of what robots.txt blocks has
    /// nothing to find. That isn't finding that nothing is blocked: the check is skipped, saying why, and has no count,
    /// where the same site crawled by its rules has its blocked URLs flagged.
    /// </summary>
    [Fact]
    public async Task WhatRobotsTxtBlocksIsSkippedWhenTheCrawlIgnoredRobotsTxt()
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 70 });
        var options = new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 };

        // By the rules: a product links the private area and a sitemap lists a URL in it, and both are flagged.
        var obeying = await CrawlAndAnalyseAsync(options);
        Assert.Empty(obeying.Skipped);
        Assert.Equal(2, obeying.Issues.Single(issue => issue.Issue == IssueCatalog.BLOCKED_BY_ROBOTS).Count);
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }

        var ignoring = await CrawlAndAnalyseAsync(options with { RespectRobotsTxt = false });

        // Sitemap discovery still read the start origin's robots.txt, so the site's own is judged; no other origin's was.
        Assert.True(ignoring.IsPartial);
        Assert.Equal(
        [
            new SkippedCheck(IssueCatalog.BLOCKED_BY_ROBOTS.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl."),
            new SkippedCheck(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED,
                "robots.txt was ignored in this crawl, so it wasn't read on the site's other origins.")
        ], ignoring.Skipped);
        Assert.Equal(["robots.txt was ignored in this crawl, so the URLs it blocks weren't looked for, nor whether it is unavailable on the site's other origins."],
            ignoring.Notes);
        Assert.Equal(IssueCatalog.All.Except([IssueCatalog.BLOCKED_BY_ROBOTS, IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN]), ignoring.Issues.Select(issue => issue.Issue));

        // As stored: the reasons are read back with the analysis, the checks have no count (never 0), and the score says two checks couldn't run.
        Assert.Equal(ignoring.Skipped, IssueReader.ReadSkippedChecks(_path));
        Assert.False(IssueReader.ReadCounts(_path).ContainsKey(IssueCatalog.BLOCKED_BY_ROBOTS.Id));
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.BLOCKED_BY_ROBOTS.Id));
        Assert.Equal(2, IssueReader.ReadResults(_path)!.Score!.Covers.ChecksSkipped);
    }

    /// <summary>
    /// The three checks of robots.txt itself on a site whose robots.txt is missing on both its origins (https, and the
    /// http one a page links to), crawled three ways. Obeying robots.txt, the crawl reads both files and the check lists
    /// both. Ignoring it with sitemaps on, only sitemap discovery asks, and only the start origin: the site's own is
    /// judged, and what another origin's does is skipped. Ignoring it with sitemaps off, robots.txt is never asked for, so
    /// every one of the three is skipped: none reads as passed on a file nobody read.
    /// </summary>
    [Fact]
    public async Task TheChecksOfRobotsTxtAreSkippedWhereACrawlThatIgnoredItNeverReadIt()
    {
        var asked = 0;
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            if (context.Request.Path.Value == "/robots.txt")
            {
                Interlocked.Increment(ref asked);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Write(context, "text/plain", "Not here");
            }

            var links = context.Request.Path.Value == "/" ? $"<a href=\"/a\">A</a> <a href=\"http://{context.Request.Host}/b\">B</a>" : "<p>Text</p>";
            return Write(context, "text/html; charset=utf-8", $"<!doctype html><html lang=\"en\"><head><title>A page</title></head><body>{links}</body></html>");
        }, https: true);
        var origin = site.BaseUri.GetLeftPart(UriPartial.Authority);
        var plainOrigin = "http" + origin["https".Length..];
        var options = new CrawlOptions { StartUrl = site.BaseUri.AbsoluteUri, MaxRetries = 0, TrustedCertificate = site.Certificate };
        IssueDefinition[] robotsChecks = [IssueCatalog.BLOCKED_BY_ROBOTS, IssueCatalog.ROBOTS_TXT_UNREACHABLE, IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN, IssueCatalog.ROBOTS_TXT_MISSING];
        List<SkippedCheck> OfRobots(AnalysisSummary analysis) => [.. analysis.Skipped.Where(skip => robotsChecks.Any(check => check.Id == skip.CheckId))];
        string? Missing() => IssueReader.ReadAll(_path, IssueCatalog.ROBOTS_TXT_MISSING.Id).SingleOrDefault()?.Detail;

        // Obeying robots.txt: both origins' files were read, and both are listed.
        var obeying = await CrawlAndAnalyseAsync(options);
        Assert.Empty(OfRobots(obeying));
        // In the order they were read, which the crawl's pace decides.
        Assert.Equal([$"{plainOrigin}/robots.txt (404 Not Found)", $"{origin}/robots.txt (404 Not Found)"],
            Missing()!.Split(", ").Order(StringComparer.Ordinal));

        // Ignoring it, sitemaps on: the start origin's was read for its Sitemap lines, and is judged; the other origin's wasn't.
        DeleteCrawl();
        asked = 0;
        var ignoring = await CrawlAndAnalyseAsync(options with { RespectRobotsTxt = false });
        Assert.Equal(
        [
            new SkippedCheck(IssueCatalog.BLOCKED_BY_ROBOTS.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl."),
            new SkippedCheck(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED,
                "robots.txt was ignored in this crawl, so it wasn't read on the site's other origins.")
        ], OfRobots(ignoring));
        Assert.Equal($"{origin}/robots.txt (404 Not Found)", Missing());
        Assert.Contains(ignoring.Issues, issue => issue is { Count: 0 } && issue.Issue == IssueCatalog.ROBOTS_TXT_UNREACHABLE);
        Assert.Equal(1, asked);

        // Ignoring it, sitemaps off: nothing asked for robots.txt, so nothing is known of it, and no check of it ran.
        DeleteCrawl();
        asked = 0;
        var unread = await CrawlAndAnalyseAsync(options with { RespectRobotsTxt = false, DiscoverSitemaps = false });
        Assert.Equal(0, asked);
        const string NEVER_READ = "robots.txt was ignored in this crawl, and nothing else asked for it, so it was never read.";
        Assert.Equal(
        [
            new SkippedCheck(IssueCatalog.BLOCKED_BY_ROBOTS.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED, "robots.txt was ignored in this crawl."),
            new SkippedCheck(IssueCatalog.ROBOTS_TXT_UNREACHABLE.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED, NEVER_READ),
            new SkippedCheck(IssueCatalog.ROBOTS_TXT_UNREACHABLE_OTHER_ORIGIN.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED,
                "robots.txt was ignored in this crawl, so it wasn't read on the site's other origins."),
            new SkippedCheck(IssueCatalog.ROBOTS_TXT_MISSING.Id, SkippedCheckReasons.ROBOTS_TXT_IGNORED, NEVER_READ)
        ], OfRobots(unread));
        Assert.Contains("robots.txt was ignored in this crawl and never read, so the URLs it blocks, and whether it is missing or unavailable, weren't looked for.", unread.Notes);
        Assert.DoesNotContain(unread.Issues, issue => robotsChecks.Contains(issue.Issue));
        Assert.DoesNotContain(IssueReader.ReadCounts(_path).Keys, id => robotsChecks.Any(check => check.Id == id));
        Assert.Equal(unread.Skipped, IssueReader.ReadSkippedChecks(_path));

        // One reason for the four checks, in words that are true of each: what the files block, and whether each can be read.
        Assert.Equal(
            new SkipReasonPlainText(
                "This site check was set to ignore your site's rules files for search engines, so it can't say what they put off limits or whether each can be read.",
                "Set the check to follow the rules files again in its Advanced settings, then check the site again."),
            PlainWords.SkipReason(SkippedCheckReasons.ROBOTS_TXT_IGNORED));
    }

    /// <summary>
    /// Whether a site hides itself from search is judged by its home page, and a crawl started anywhere else has none: a
    /// crawl of a shop's section, whose first page and listings are all noindex, would be called a site hidden from
    /// search, at 60 points. The check is skipped there, saying why: not passed, and not fired. A start URL at the root is
    /// still the root with a query, and where it redirects: the home page is the page it ends on.
    /// </summary>
    [Theory]
    [InlineData("shop/", "/shop/")]
    [InlineData("home", "/home")]
    [InlineData("?lang=en", null)]
    [InlineData("", null)]
    public async Task ASiteHiddenFromSearchIsSkippedOnACrawlThatDidNotStartAtTheSitesRoot(string start, string? skippedFor)
    {
        const string NOINDEX = "<meta name=\"robots\" content=\"noindex\">";
        await using var site = await LoopbackServer.StartAsync(context =>
        {
            // The root moves to /en/, as a site with a language in its paths does; every page is noindex.
            var path = context.Request.Path.Value!;
            if (path == "/" && !context.Request.QueryString.HasValue)
            {
                context.Response.Redirect("/en/", permanent: true);
                return Task.CompletedTask;
            }

            if (path == "/robots.txt")
            {
                return Write(context, "text/plain", "User-agent: *\nAllow: /");
            }

            var folder = path.EndsWith('/') ? path : "/";
            var links = path is "/" or "/en/" or "/shop/" or "/home" ? $"<a href=\"{folder}a\">A</a> <a href=\"{folder}b\">B</a> <a href=\"{folder}c\">C</a>" : "<p>Text</p>";
            return Write(context, "text/html; charset=utf-8", $"<!doctype html><html lang=\"en\"><head><title>A page</title>{NOINDEX}</head><body>{links}</body></html>");
        });

        var analysis = await CrawlAndAnalyseAsync(new CrawlOptions { StartUrl = new Uri(site.BaseUri, start).AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false });

        // Four pages read, every one noindex: the start page and the three it links.
        Assert.Equal(4, IssueReader.ReadCounts(_path)[IssueCatalog.NOINDEX_PAGE.Id]);
        var hidden = IssueReader.ReadAll(_path, IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id).ToList();
        var skip = analysis.Skipped.SingleOrDefault(skipped => skipped.CheckId == IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id);
        if (skippedFor is null)
        {
            // From the root: judged, and reported on the page the root ends on.
            Assert.Null(skip);
            var hit = Assert.Single(hidden);
            Assert.Equal((new Uri(site.BaseUri, start.Length == 0 ? "en/" : start).AbsoluteUri, "4 of 4 pages are noindex"), (hit.Url, hit.Detail));
            return;
        }

        Assert.Equal(new SkippedCheck(IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id, SkippedCheckReasons.START_NOT_ROOT,
            $"The crawl started at {skippedFor}, not at the site's root, so its first page isn't known to be the site's home page."), skip);
        Assert.Contains($"A site hidden from search wasn't looked for: the crawl started at {skippedFor}, not at the site's root, so its first page isn't known to be the site's home page.",
            analysis.Notes);
        Assert.Empty(hidden);
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.SITE_HIDDEN_FROM_SEARCH);
        Assert.False(IssueReader.ReadCounts(_path).ContainsKey(IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id));
        Assert.DoesNotContain(IssueReader.ReadResults(_path)!.Score!.Lines, line => line.CheckId == IssueCatalog.SITE_HIDDEN_FROM_SEARCH.Id);
        Assert.Equal(
            new SkipReasonPlainText(
                "This site check started from a page inside your site, not from its home page, so it can't judge the site as a whole.",
                "Start the check from your home page, then check the site again."),
            PlainWords.SkipReason(SkippedCheckReasons.START_NOT_ROOT));
    }

    private void DeleteCrawl()
    {
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The near-duplicate check compares 256-bit fingerprints, of which an earlier version recorded only the first 64: a
    /// crawl it made has no columns for the rest, and pages it fetched while continuing a crawl have none in them. Either way
    /// the check is skipped, saying why, and nothing is stored for it, while exact duplicates are still found. The crawl's
    /// file still opens and reads as before.
    /// </summary>
    [Theory]
    [InlineData(true, "This crawl was made by an earlier version")]
    [InlineData(false, "1 page in this crawl was fetched by an earlier version")]
    public async Task NearDuplicatesAreSkippedWithoutTheWholeFingerprint(bool madeBefore, string why)
    {
        await using var server = await SyntheticSiteServer.StartAsync(new SyntheticSiteOptions { PageCount = 60 });
        await CrawlAndAnalyseAsync(new CrawlOptions { StartUrl = server.BaseUri.AbsoluteUri, Concurrency = 8 });
        Assert.Equal(2, IssueReader.ReadAll(_path, IssueCatalog.NEAR_DUPLICATE_CONTENT.Id).Count());

        Execute(madeBefore
            ? string.Concat(CrawlSchema.RESPONSE_COLUMNS.Select(column => $"ALTER TABLE responses DROP COLUMN {column.Name};"))
            : "UPDATE responses SET simhash_1 = NULL, simhash_2 = NULL, simhash_3 = NULL WHERE url_id = (SELECT id FROM urls WHERE url LIKE '%/plants/content-near-a')");
        var analysis = await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);

        Assert.True(analysis.IsPartial);
        Assert.Contains($"Near-duplicate content wasn't checked: {char.ToLowerInvariant(why[0])}{why[1..]}, which didn't record what the check needs. Crawl again to check it.",
            analysis.Notes);
        Assert.Equal(new SkippedCheck(IssueCatalog.NEAR_DUPLICATE_CONTENT.Id, SkippedCheckReasons.EARLIER_VERSION,
            $"{why}, which didn't record what this check needs. Crawl again to check it."), Assert.Single(analysis.Skipped));
        Assert.DoesNotContain(analysis.Issues, issue => issue.Issue == IssueCatalog.NEAR_DUPLICATE_CONTENT);
        Assert.Empty(IssueReader.ReadAll(_path, IssueCatalog.NEAR_DUPLICATE_CONTENT.Id));
        Assert.Equal(2, analysis.Issues.Single(issue => issue.Issue == IssueCatalog.DUPLICATE_CONTENT).Count);
        Assert.Equal(server.Site.GetExpectations().HtmlPages, CrawlStatsReader.Read(_path).HtmlPageCount);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task<AnalysisSummary> CrawlAndAnalyseAsync(CrawlOptions options)
    {
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance).Start(new CrawlRequest(options, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await job.Completion.WaitAsync(TimeSpan.FromMinutes(1))).Status);
        }

        return await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);
    }

    private async Task<AnalysisSummary> CrawlOnAFakeClockAndAnalyseAsync(CrawlOptions options)
    {
        var time = new FakeTimeProvider();
        await using (var job = new CrawlEngine(NullLoggerFactory.Instance, time).Start(new CrawlRequest(options, _path)))
        {
            Assert.Equal(CrawlStatus.Completed, (await FakeClock.PumpAsync(time, job.Completion, TimeSpan.FromSeconds(1), realLimit: TimeSpan.FromMinutes(2))).Status);
        }

        return await new CrawlAnalyzer(NullLoggerFactory.Instance).AnalyzeAsync(_path);
    }

    private static Task Write(HttpContext context, string contentType, string body)
    {
        context.Response.ContentType = contentType;
        return context.Response.WriteAsync(body);
    }
}
