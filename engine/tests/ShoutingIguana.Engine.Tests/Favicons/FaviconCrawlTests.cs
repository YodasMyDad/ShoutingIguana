using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Favicons;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Tests.Fetching;
using ShoutingIguana.Engine.Tests.Support;

namespace ShoutingIguana.Engine.Tests.Favicons;

/// <summary>
/// Crawls of a small loopback site for its icon: which one is kept, what is asked of the site for it, what a site can
/// send in an icon's place, and that the icon is never one of the crawl's URLs nor anything a crawl waits for.
/// </summary>
public sealed class FaviconCrawlTests : IAsyncLifetime
{
    private const string HOME = "<html><head><title>Home</title>ICONS</head><body><a href=\"/about\">About</a></body></html>";

    private readonly CrawlFiles _files = new();
    private readonly ConcurrentDictionary<string, Func<HttpContext, Task>> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _asked = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LoopbackServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = await LoopbackServer.StartAsync(RespondAsync);
        Serve("/robots.txt", "text/plain", "User-agent: *\nDisallow:"u8.ToArray());
        Serve("/about", "text/html", "<html><head><title>About</title></head><body>About us.</body></html>"u8.ToArray());
    }

    public async Task DisposeAsync()
    {
        _release.TrySetResult();
        await _server.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task TheSmallestIconOfSixtyFourPixelsOrMoreThatThePageLinksIsKept()
    {
        Home("""
            <link rel="icon" href="/icons/16.png" sizes="16x16">
            <link rel="icon" href="/icons/512.png" sizes="512x512">
            <link rel="icon" href="/icons/192.png" sizes="192x192" type="image/png">
            <link rel="icon" href="/icons/vector.svg" sizes="any" type="image/svg+xml">
            <link rel="apple-touch-icon" href="/icons/touch.png" sizes="180x180">
            """);
        foreach (var size in new[] { 16, 192, 512 })
        {
            Serve($"/icons/{size}.png", "image/png", TestImages.Png(size, size));
        }

        Serve("/icons/touch.png", "image/png", TestImages.Png(180, 180));
        Serve("/icons/vector.svg", "image/svg+xml", TestImages.Svg());
        Serve("/favicon.ico", "image/x-icon", TestImages.Ico(false, (32, 32)));

        var path = await CrawlAsync();

        var icon = FaviconReader.Read(path)!;
        Assert.Equal((_server.Url("/icons/192.png").AbsoluteUri, "image/png", 192, 192), (icon.Url, icon.ContentType, icon.Width, icon.Height));
        Assert.Equal(TestImages.Png(192, 192), icon.Bytes);
        Assert.InRange(icon.FetchedAt, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
        Assert.True(CrawlSummaryReader.Read(path).HasFavicon);

        // The crawl checked each icon the page links, as it checks every link: once. The one kept was asked once more,
        // for its bytes, and nothing else was: not the other sizes, never the SVG, and not /favicon.ico, which no page links.
        Assert.Equal(2, Asked("/icons/192.png"));
        Assert.All(new[] { "/icons/16.png", "/icons/512.png", "/icons/touch.png", "/icons/vector.svg" }, linked => Assert.Equal(1, Asked(linked)));
        Assert.Equal(0, Asked("/favicon.ico"));
    }

    [Fact]
    public async Task WithNoIconLinkedFaviconIcoIsKeptWhateverTheSiteCallsItAndIsNoUrlOfTheCrawl()
    {
        Home(string.Empty);
        var ico = TestImages.Ico(false, (16, 16), (48, 48));
        Serve("/favicon.ico", "text/html; charset=utf-8", ico);

        var path = await CrawlAsync();

        var icon = FaviconReader.Read(path)!;
        Assert.Equal((_server.Url("/favicon.ico").AbsoluteUri, "image/x-icon", 48, 48), (icon.Url, icon.ContentType, icon.Width, icon.Height));
        Assert.Equal(ico, icon.Bytes);
        Assert.Equal(1, Asked("/favicon.ico"));

        // The fetch is in no table but its own: the crawl has its two pages (and its probe of the site's https version),
        // the one link between them, and nothing that names the icon.
        Assert.Equal(["/", "/about"], TestCrawls.Rows(path, $"SELECT url FROM urls WHERE source <> {(int)DiscoverySource.SiteVersion}").Select(url => new Uri(url).AbsolutePath));
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM urls WHERE url LIKE '%favicon%'"));
        Assert.Equal(2, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
        Assert.Equal(1, TestCrawls.Count(path, "SELECT COUNT(*) FROM links"));
        Assert.Equal(2, CrawlSummaryReader.Read(path).PageCount);
        Assert.Equal(1, TestCrawls.Count(path, "SELECT COUNT(*) FROM favicon"));
    }

    [Fact]
    public async Task WhatIsNotAnImageIsPassedOverForTheNextBest()
    {
        // In the order they're tried: an SVG called a PNG, a page called an icon, a PNG too big to keep. Then /favicon.ico.
        Home("""
            <link rel="icon" href="/a.png" sizes="64x64" type="image/png">
            <link rel="icon" href="/b.ico" sizes="96x96">
            <link rel="icon" href="/c.png" sizes="128x128">
            <link rel="icon" href="/d.png" sizes="256x256">
            """);
        Serve("/a.png", "image/png", TestImages.Svg());
        Serve("/b.ico", "image/x-icon", "<html><script>alert(1)</script></html>"u8.ToArray());
        Serve("/c.png", "image/png", TestImages.Png(128, 128, FaviconSniffer.MAX_BYTES + 1));
        Serve("/d.png", "image/png", TestImages.Png(256, 256));
        Serve("/favicon.ico", "image/gif", TestImages.Gif(32, 32));

        var path = await CrawlAsync();

        var icon = FaviconReader.Read(path)!;
        Assert.Equal((_server.Url("/favicon.ico").AbsoluteUri, "image/gif", 32, 32), (icon.Url, icon.ContentType, icon.Width, icon.Height));

        // Three of the page's icons were tried, in order, and no more of them: the fourth was only checked, as a link.
        Assert.Equal((2, 2, 2, 1, 1), (Asked("/a.png"), Asked("/b.ico"), Asked("/c.png"), Asked("/d.png"), Asked("/favicon.ico")));
    }

    [Fact]
    public async Task ASiteWithNoIconTheEngineKeepsHasNoneAndItsCrawlCompletes()
    {
        Home("""<link rel="icon" href="/icon.svg" type="image/svg+xml"><link rel="icon" href="/huge.png">""");
        Serve("/icon.svg", "image/svg+xml", TestImages.Svg());
        Serve("/huge.png", "image/png", TestImages.Png(4_000, 4_000));

        var path = await CrawlAsync();

        Assert.Null(FaviconReader.Read(path));
        Assert.False(CrawlSummaryReader.Read(path).HasFavicon);
        Assert.Equal(0, TestCrawls.Count(path, "SELECT COUNT(*) FROM favicon"));

        // The SVG was checked as the link it is, and never asked for as an icon.
        Assert.Equal((1, 2, 1), (Asked("/icon.svg"), Asked("/huge.png"), Asked("/favicon.ico")));
    }

    [Fact]
    public async Task RobotsTxtIsObeyedForTheIconAsForAnyUrl()
    {
        Home("""<link rel="icon" href="/assets/icon.png" sizes="64x64">""");
        Serve("/robots.txt", "text/plain", "User-agent: *\nDisallow: /assets/\nDisallow: /favicon.ico"u8.ToArray());
        Serve("/assets/icon.png", "image/png", TestImages.Png(64, 64));
        Serve("/favicon.ico", "image/png", TestImages.Png(32, 32));

        var obeyed = await CrawlAsync();

        Assert.Null(FaviconReader.Read(obeyed));
        Assert.Equal((0, 0), (Asked("/assets/icon.png"), Asked("/favicon.ico")));

        // A crawl told to ignore robots.txt asks, as it asks for every URL.
        var ignored = await CrawlAsync(options => options with { RespectRobotsTxt = false });
        Assert.Equal(_server.Url("/assets/icon.png").AbsoluteUri, FaviconReader.Read(ignored)!.Url);
    }

    [Fact]
    public async Task ARedirectIsFollowedAFewHopsAndTheIconIsWhereItLed()
    {
        Home("""<link rel="icon" href="/loop" sizes="64x64">""");
        _routes["/loop"] = context => Redirect(context, "/loop");
        _routes["/favicon.ico"] = context => Redirect(context, "/moved/icon");
        _routes["/moved/icon"] = context => Redirect(context, "/real.png");
        Serve("/real.png", "application/octet-stream", TestImages.Png(48, 48));

        var path = await CrawlAsync();

        var icon = FaviconReader.Read(path)!;
        Assert.Equal((_server.Url("/real.png").AbsoluteUri, "image/png", 48, 48), (icon.Url, icon.ContentType, icon.Width, icon.Height));

        // A redirect that never ends is given up on: the crawl's own check of the link (it follows its loop as it does
        // any redirect), and the icon's four requests.
        Assert.InRange(Asked("/loop"), 5, 12);
    }

    [Fact]
    public async Task AnotherSitesIconIsAskedForOnlyWhenTheCrawlChecksOtherSites()
    {
        Home($"""<link rel="icon" href="{_server.OtherUrl("/cdn/icon.png")}" sizes="64x64">""");
        Serve("/cdn/icon.png", "image/png", TestImages.Png(64, 64));
        Serve("/favicon.ico", "image/png", TestImages.Png(32, 32));

        var kept = await CrawlAsync(options => options with { CheckExternalLinks = false });
        Assert.Equal(_server.Url("/favicon.ico").AbsoluteUri, FaviconReader.Read(kept)!.Url);
        Assert.Equal(0, Asked("/cdn/icon.png", other: true) + Asked("/robots.txt", other: true));

        var checkedToo = await CrawlAsync();
        Assert.Equal(_server.OtherUrl("/cdn/icon.png").AbsoluteUri, FaviconReader.Read(checkedToo)!.Url);
        Assert.Equal((2, 0), (Asked("/cdn/icon.png", other: true), Asked("/favicon.ico")));
    }

    /// <summary>
    /// An icon that never arrives costs a completed crawl the few seconds it gives one, not the request's timeout, and
    /// costs it nothing else: the crawl completes, with its pages, and has no icon.
    /// </summary>
    [Fact]
    public async Task AnIconThatNeverArrivesNeitherFailsACrawlNorHoldsItUp()
    {
        Home(string.Empty);
        _routes["/favicon.ico"] = async context =>
        {
            await context.Response.Body.FlushAsync();
            await _release.Task.WaitAsync(context.RequestAborted);
        };

        var clock = Stopwatch.StartNew();
        var path = await CrawlAsync(options => options with { RequestTimeoutSeconds = 120 });

        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(60));
        Assert.Null(FaviconReader.Read(path));
        Assert.Equal(2, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
        Assert.Equal(1, Asked("/favicon.ico"));
    }

    /// <summary>
    /// A crawl that is continued keeps the icon it has, and asks for none: the site's icon is fetched once, by the run
    /// that reads the start page.
    /// </summary>
    [Fact]
    public async Task AContinuedCrawlKeepsTheIconItHasAndAsksForNoOther()
    {
        Home(string.Empty);
        Serve("/favicon.ico", "image/png", TestImages.Png(32, 32));

        // Stopped once it has its icon, with a page still on its way, so the run that continues it has a page to read.
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var about = _routes["/about"];
        _routes["/about"] = context => stopped.Task.WaitAsync(context.RequestAborted);
        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(Options(), path))
        {
            await TestCrawls.WaitUntilAsync(path, "SELECT COUNT(*) FROM favicon");
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync().WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        stopped.TrySetResult();
        Assert.Equal((32, 1), (FaviconReader.Read(path)!.Width, Asked("/favicon.ico")));

        _asked.Clear();
        _routes["/about"] = about;
        Serve("/favicon.ico", "image/png", TestImages.Png(64, 64));
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), path, resume: true)).Status);

        Assert.Equal((32, 0, 1), (FaviconReader.Read(path)!.Width, Asked("/favicon.ico"), Asked("/about")));
        Assert.Equal(2, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
    }

    /// <summary>
    /// The first page of the site a run reads names the icon. That is the start page, unless it never answers with a
    /// page: then it's the first page the crawl does read, and /favicon.ico is looked for where that page is.
    /// </summary>
    [Fact]
    public async Task WhenTheStartPageIsNoPageTheFirstPageReadNamesTheIcon()
    {
        _routes["/"] = context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        };
        Serve("/robots.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes($"User-agent: *\nDisallow:\nSitemap: {_server.Url("/sitemap.xml")}"));
        Serve("/sitemap.xml", "application/xml", System.Text.Encoding.UTF8.GetBytes(
            $"<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{_server.Url("/about")}</loc></url></urlset>"));
        Serve("/about", "text/html", "<html><head><title>About</title><link rel=\"icon\" href=\"/about.png\" sizes=\"64x64\"></head><body>About us.</body></html>"u8.ToArray());
        Serve("/about.png", "image/png", TestImages.Png(64, 64));

        var path = await CrawlAsync(options => options with { DiscoverSitemaps = true });

        Assert.Equal(_server.Url("/about.png").AbsoluteUri, FaviconReader.Read(path)!.Url);
    }

    /// <summary>
    /// A crawl file is trusted no more than a site: whatever a file holds as its icon, only an image the engine keeps is
    /// ever read from it, as what its bytes say it is.
    /// </summary>
    [Fact]
    public async Task WhatACrawlFileHoldsAsItsIconIsToldByItsBytesAgain()
    {
        Home(string.Empty);
        Serve("/favicon.ico", "image/png", TestImages.Png(32, 32));
        var path = await CrawlAsync();

        // The file says it's a page (in a table made over without the engine's constraints, as a crafted file's would be):
        // the bytes say it's a PNG, and that is what's given.
        Change(path, "CREATE TABLE crafted AS SELECT * FROM favicon; DROP TABLE favicon; ALTER TABLE crafted RENAME TO favicon", null);
        Change(path, "UPDATE favicon SET content_type = 'text/html', width = 9, height = 9, url = NULL, fetched_at = 'never'", null);
        var icon = FaviconReader.Read(path)!;
        Assert.Equal(("image/png", 32, 32, string.Empty, DateTimeOffset.UnixEpoch), (icon.ContentType, icon.Width, icon.Height, icon.Url, icon.FetchedAt));

        // The file says it's a PNG: the bytes are an SVG, a page, text, nothing, or more than an icon may be.
        foreach (var body in new[] { TestImages.Svg(), "<html><script>alert(1)</script></html>"u8.ToArray(), [], TestImages.Png(32, 32, FaviconSniffer.MAX_BYTES + 1) })
        {
            Change(path, "UPDATE favicon SET content_type = 'image/png', body = $body", body);
            Assert.Null(FaviconReader.Read(path));
        }

        Change(path, "UPDATE favicon SET body = 'iVBORw0KGgo'", null);
        Assert.Null(FaviconReader.Read(path));
        Assert.False(CrawlSummaryReader.Read(path).HasFavicon);

        // A table of another program's making, and a file from before icons were kept: none, and nothing fails.
        Change(path, "DROP TABLE favicon; CREATE TABLE favicon (id INTEGER PRIMARY KEY, picture BLOB)", null);
        Assert.Null(FaviconReader.Read(path));
        Assert.False(CrawlSummaryReader.Read(path).HasFavicon);
        Change(path, "DROP TABLE favicon", null);
        Assert.Null(FaviconReader.Read(path));
        Assert.False(CrawlSummaryReader.Read(path).HasFavicon);
    }

    /// <summary>
    /// A file whose favicon table is another program's is crawled as any other, and keeps no icon: none is asked for,
    /// and nothing is written to that table.
    /// </summary>
    [Fact]
    public async Task ATableOfAnotherProgramsMakingCostsTheCrawlItsIconAndNothingElse()
    {
        Serve("/favicon.ico", "image/png", TestImages.Png(32, 32));

        // A table of another shape: no icon is asked for, and nothing is written to it.
        var foreign = await StoppedBeforeItsStartPageAsync();
        Change(foreign, "DROP TABLE favicon; CREATE TABLE favicon (id INTEGER PRIMARY KEY, picture BLOB NOT NULL)", null);
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), foreign, resume: true)).Status);
        Assert.Equal(2, TestCrawls.Count(foreign, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
        Assert.Equal((0, 0L), (Asked("/favicon.ico"), TestCrawls.Count(foreign, "SELECT COUNT(*) FROM favicon")));
        Assert.Null(FaviconReader.Read(foreign));

        // A table of the engine's columns that refuses the row all the same: the icon is lost, the crawl isn't.
        var refusing = await StoppedBeforeItsStartPageAsync();
        Change(refusing, """
            DROP TABLE favicon;
            CREATE TABLE favicon (id INTEGER PRIMARY KEY, url TEXT, content_type TEXT, width INTEGER, height INTEGER, fetched_at INTEGER, body BLOB CHECK (length(body) < 8))
            """, null);
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), refusing, resume: true)).Status);
        Assert.Equal(2, TestCrawls.Count(refusing, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
        Assert.Equal((1, 0L), (Asked("/favicon.ico"), TestCrawls.Count(refusing, "SELECT COUNT(*) FROM favicon")));

        // And a file from before icons were kept, continued by a run that reads its start page, gains one.
        var earlier = await StoppedBeforeItsStartPageAsync();
        Change(earlier, "DROP TABLE favicon", null);
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(Options(), earlier, resume: true)).Status);
        Assert.Equal(32, FaviconReader.Read(earlier)!.Width);
    }

    /// <summary>A crawl stopped while its start page was still on its way, so the run that continues it is the one that reads it.</summary>
    private async Task<string> StoppedBeforeItsStartPageAsync()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _routes["/"] = async context =>
        {
            asked.TrySetResult();
            await stopped.Task.WaitAsync(context.RequestAborted);
        };

        var path = _files.NewPath();
        await using (var job = TestCrawls.Start(Options(), path))
        {
            await asked.Task.WaitAsync(TestCrawls.TIMEOUT);
            Assert.Equal(CrawlStatus.Stopped, (await job.StopAsync().WaitAsync(TestCrawls.TIMEOUT)).Status);
        }

        stopped.TrySetResult();
        Assert.Equal(0, TestCrawls.Count(path, $"SELECT COUNT(*) FROM responses WHERE outcome = {(int)FetchOutcome.Success}"));
        _asked.Clear();
        Home(string.Empty);
        return path;
    }

    private CrawlOptions Options() => new() { StartUrl = _server.BaseUri.AbsoluteUri, MaxRetries = 0, DiscoverSitemaps = false };

    private async Task<string> CrawlAsync(Func<CrawlOptions, CrawlOptions>? change = null)
    {
        _asked.Clear();
        var path = _files.NewPath();
        var options = Options();
        Assert.Equal(CrawlStatus.Completed, (await TestCrawls.RunAsync(change?.Invoke(options) ?? options, path)).Status);
        return path;
    }

    private void Home(string icons) => Serve("/", "text/html", System.Text.Encoding.UTF8.GetBytes(HOME.Replace("ICONS", icons, StringComparison.Ordinal)));

    private void Serve(string path, string contentType, byte[] body) => _routes[path] = async context =>
    {
        context.Response.ContentType = contentType;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    };

    private static Task Redirect(HttpContext context, string location)
    {
        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }

    /// <summary>How often a path was asked for, on the site or on the other site.</summary>
    private int Asked(string path, bool other = false) => _asked.GetValueOrDefault($"{(other ? _server.OtherBaseUri.Port : _server.BaseUri.Port)}{path}");

    private Task RespondAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        _asked.AddOrUpdate($"{context.Connection.LocalPort}{path}", 1, (_, count) => count + 1);
        if (_routes.TryGetValue(path, out var respond))
        {
            return respond(context);
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static void Change(string path, string sql, byte[]? body)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (body is not null)
        {
            command.Parameters.AddWithValue("$body", body);
        }

        command.ExecuteNonQuery();
    }
}
