using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// What the browser hands back for pages that are awkward to render: a router that rewrites the address, a script
/// that leaves the page, content in a shadow root, content that waits to be scrolled to, a service worker, requests
/// the crawl's own pace holds back, and a browser that died.
/// </summary>
public sealed class JavaScriptRendererTests
{
    // A script builds each marker a test looks for from two halves, so the marker is in the HTML only once it has run.
    private const string WORDS = "Plain words that a reader sees on the page, written by its script.";

    private static readonly Dictionary<string, string> PAGES = new(StringComparer.Ordinal)
    {
        // A single-page app's router puts the address it wants in the bar. No other document is loaded.
        ["/router"] = Page("""
            <div id="app"></div><script>
            history.replaceState({}, '', '/router/home');
            document.getElementById('app').innerHTML = '<h1>Rou' + 'ted</h1><a href="/found-by-' + 'router">x</a>';
            </script>
            """),
        ["/leaves"] = Page("""<div id="app"></div><script>location.replace('/landed');</script>"""),
        ["/landed"] = Page("<h1>Landed</h1>"),
        ["/refreshes"] = Page("""
            <div id="app"></div><script>
            const meta = document.createElement('meta');
            meta.httpEquiv = 'refresh';
            meta.content = '0; url=/landed';
            document.head.appendChild(meta);
            </script>
            """),
        ["/leaves-late"] = Page("""<div id="app"></div><script>setTimeout(() => { location.href = '/landed'; }, 300);</script>"""),
        ["/leaves-for-nothing"] = Page("""<div id="app"></div><script>location.replace('/gone');</script>"""),
        ["/bundled"] = Page("""<link rel="stylesheet" href="/site.css"><div id="app"></div><script src="/bundle.js"></script><script src="/fresh.js"></script>"""),
        ["/bundled-too"] = Page("""<link rel="stylesheet" href="/site.css"><div id="app"></div><script src="/bundle.js"></script><script src="/fresh.js"></script>"""),
        ["/shadow"] = Page("""
            <div id="app"></div><my-card><span slot="name">Slotted name</span><i>never shown</i></my-card><script>
            customElements.define('my-card', class extends HTMLElement {
              connectedCallback() {
                const root = this.attachShadow({ mode: 'open' });
                root.innerHTML = '<h2>In the ' + 'shadow</h2><slot name="name"></slot><slot name="empty">Fallback ' + 'words</slot><a href="/found-in-' + 'shadow">x</a><inner-part></inner-part>';
              }
            });
            customElements.define('inner-part', class extends HTMLElement {
              connectedCallback() { this.attachShadow({ mode: 'open' }).innerHTML = '<p>Nested ' + 'shadow</p>'; }
            });
            </script>
            """),
        ["/below"] = Page("""
            <h1>Top</h1><div style="height:3000px"></div><div id="sentinel"></div><script>
            new IntersectionObserver((entries, observer) => {
              if (entries.some(entry => entry.isIntersecting)) {
                observer.disconnect();
                document.body.insertAdjacentHTML('beforeend', '<a href="/found-' + 'below">Seen once scrolled to</a>');
              }
            }).observe(document.getElementById('sentinel'));
            </script>
            """),
        ["/worker"] = Page("""
            <h1>Worker</h1><script>
            navigator.serviceWorker.register('/sw.js').then(() => fetch('/after-register'), () => fetch('/after-register'));
            </script>
            """),
        // The data arrives through three requests, one after another, as a page that loads its route, then its data.
        ["/chained"] = Page("""
            <div id="app"></div><script>
            fetch('/data?n=1').then(() => fetch('/data?n=2')).then(() => fetch('/data?n=3')).then(() => {
              document.getElementById('app').innerHTML = '<h1>Chai' + 'ned</h1>';
            });
            </script>
            """),
        ["/plain"] = Page("<h1>Plain</h1>"),
        ["/asks-ahead"] = Page("""
            <link rel="prefetch" href="/next-page.js"><div id="app"></div><script>
            fetch('/next-route', { headers: { 'Next-Router-Prefetch': '1' } }).catch(() => {});
            fetch('/data').then(() => { document.getElementById('app').innerHTML = '<h1>Asked ' + 'ahead</h1>'; });
            </script>
            """),
        ["/asks"] = Page("""
            <div id="app"></div><script>
            Promise.allSettled([fetch('/data'), fetch('/kept-out/data')]).then(answers => {
              document.getElementById('app').innerHTML = '<h1>As' + 'ked ' + answers.map(answer => answer.status).join(' ') + '</h1>';
            });
            </script>
            """),
        ["/members"] = Page("""<div id="app"></div><script>document.getElementById('app').innerHTML = '<h1>Mem' + 'bers</h1>';</script>""")
    };

    private readonly ConcurrentQueue<string> _requests = new();

    [Fact]
    public async Task APageWhoseRouterRewritesItsAddressIsStillRendered()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var html = await HtmlAsync(renderer, site.Url("/router"));

        Assert.NotNull(html);
        Assert.Contains("<h1>Routed</h1>", html, StringComparison.Ordinal);
        Assert.Contains("/found-by-router", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageItsScriptLeavesNamesWhereItWentAndTheCrawlIsToldOnce()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer);

        var left = await renderer.RenderAsync(site.Url("/leaves"), CancellationToken.None);
        Assert.Null(left.Html);
        Assert.Equal(site.Url("/landed"), left.LeftFor);

        // A meta refresh the script writes is the same thing, and so is leaving a moment after the page has loaded.
        Assert.Equal(site.Url("/landed"), (await renderer.RenderAsync(site.Url("/refreshes"), CancellationToken.None)).LeftFor);
        Assert.Equal(site.Url("/landed"), (await renderer.RenderAsync(site.Url("/leaves-late"), CancellationToken.None)).LeftFor);

        // Where it lands doesn't have to answer: the crawl finds that out when it fetches it.
        Assert.Equal(site.Url("/gone"), (await renderer.RenderAsync(site.Url("/leaves-for-nothing"), CancellationToken.None)).LeftFor);

        var notice = Assert.Single(observer.Seen, notice => notice.Code == "javascript-redirect");
        Assert.Equal(CrawlNoticeLevel.Info, notice.Level);
        Assert.Contains(site.Url("/leaves").AbsoluteUri, notice.Message, StringComparison.Ordinal);
        Assert.Contains(site.Url("/landed").AbsoluteUri, notice.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A script and a stylesheet the first page was sent are not asked for again by the second, which still runs
    /// the script. One the site marks no-store is asked for every time. A data request is never kept.
    /// </summary>
    [Fact]
    public async Task AScriptOnePageWasSentIsNotAskedForAgainByTheNext()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var first = await HtmlAsync(renderer, site.Url("/bundled"));
        var second = await HtmlAsync(renderer, site.Url("/bundled-too"));

        Assert.Contains("<h1>Bundled</h1>", first, StringComparison.Ordinal);
        Assert.Contains("<h1>Bundled</h1>", second, StringComparison.Ordinal);
        Assert.Equal(1, _requests.Count(path => path == "/bundle.js"));
        Assert.Equal(1, _requests.Count(path => path == "/site.css"));
        Assert.Equal(2, _requests.Count(path => path == "/fresh.js"));
        Assert.Equal(2, _requests.Count(path => path == "/data"));

        var counts = renderer.Counts;
        Assert.Equal(2, counts.Pages);
        Assert.Equal(2, counts.FromCache);
        Assert.Equal(8, counts.Sent);
        Assert.True(counts.CacheBytes > 0);
    }

    /// <summary>
    /// A page that asks ahead for where a visitor might go next (a prefetch link, or Next.js's router) is not sent
    /// those: they are other pages' files, and on docusaurus.io they were 93 of the 108 requests a page made.
    /// </summary>
    [Fact]
    public async Task WhatAPageAsksForAheadOfAVisitIsNotFetched()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var html = await HtmlAsync(renderer, site.Url("/asks-ahead"));

        Assert.Contains("<h1>Asked ahead</h1>", html, StringComparison.Ordinal);
        Assert.Contains("/data", _requests);
        Assert.DoesNotContain("/next-page.js", _requests);
        Assert.DoesNotContain("/next-route", _requests);
    }

    /// <summary>A kept script is still held to robots.txt: what the crawler is kept out of is not run from memory either.</summary>
    [Fact]
    public async Task AKeptScriptIsStillHeldToRobotsTxt()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);
        Assert.Contains("<h1>Bundled</h1>", await HtmlAsync(renderer, site.Url("/bundled")), StringComparison.Ordinal);

        renderer.ReadRobotsTxt = _ => Task.FromResult(RobotsTxtParser.Parse("User-agent: *" + Environment.NewLine + "Disallow: /bundle.js", "ShoutingIguana"));
        var after = await HtmlAsync(renderer, site.Url("/bundled-too"));

        Assert.NotNull(after);
        Assert.DoesNotContain("<h1>Bundled</h1>", after, StringComparison.Ordinal);
        Assert.Equal(1, _requests.Count(path => path == "/bundle.js"));
    }

    [Fact]
    public void TheCacheStaysInsideItsBudgetAndKeepsOnlyWhatASiteLetsBeKept()
    {
        var cache = new RenderCache(budgetBytes: 1_000);
        var plain = new Dictionary<string, string> { ["content-type"] = "text/javascript", ["content-encoding"] = "gzip", ["content-length"] = "12" };

        Assert.True(cache.Offer("https://example.com/a.js", 200, plain, new byte[400]));
        Assert.True(cache.Offer("https://example.com/b.js", 200, plain, new byte[400]));
        Assert.True(cache.TryGet("https://example.com/a.js", out var kept));
        Assert.Equal(["content-type"], kept.Headers.Select(header => header.Key));

        // A third goes over the budget: the one asked for longest ago is dropped, which is b.
        Assert.True(cache.Offer("https://example.com/c.js", 200, plain, new byte[400]));
        Assert.False(cache.TryGet("https://example.com/b.js", out _));
        Assert.True(cache.TryGet("https://example.com/a.js", out _));
        Assert.True(cache.TryGet("https://example.com/c.js", out _));
        Assert.Equal(800, cache.Bytes);

        Assert.False(cache.Offer("https://example.com/big.js", 200, plain, new byte[1_001]));
        Assert.False(cache.Offer("https://example.com/missing.js", 404, plain, new byte[10]));
        Assert.False(cache.Offer("https://example.com/private.js", 200, new Dictionary<string, string> { ["Cache-Control"] = "private, no-store" }, new byte[10]));
        Assert.False(cache.Offer("https://example.com/session.js", 200, new Dictionary<string, string> { ["Set-Cookie"] = "a=b" }, new byte[10]));
        Assert.False(cache.Offer("https://example.com/varies.js", 200, new Dictionary<string, string> { ["Vary"] = "Accept-Encoding, Cookie" }, new byte[10]));
        Assert.Equal(800, cache.Bytes);

        // Ten thousand more never take it past the budget.
        for (var index = 0; index < 10_000; index++)
        {
            cache.Offer($"https://example.com/chunk-{index}.js", 200, plain, new byte[300]);
            Assert.True(cache.Bytes <= 1_000);
        }

        Assert.True(RenderCache.Serves("GET", "script"));
        Assert.True(RenderCache.Serves("GET", "stylesheet"));
        Assert.False(RenderCache.Serves("POST", "script"));
        Assert.False(RenderCache.Serves("GET", "document"));
        Assert.False(RenderCache.Serves("GET", "fetch"));
        Assert.False(RenderCache.Serves("GET", "xhr"));
    }

    [Theory]
    [InlineData(5, 32, 64L, 5)]    // the crawl's own setting
    [InlineData(16, 32, 64L, 8)]   // never more than eight
    [InlineData(16, 4, 64L, 2)]    // half the processors
    [InlineData(16, 32, 2L, 4)]    // a slot for each half gigabyte
    [InlineData(1, 32, 64L, 1)]
    [InlineData(5, 1, 1L, 1)]      // never fewer than one
    public void AsManyPagesRenderAtOnceAsTheCrawlAsksAndTheMachineAllows(int concurrency, int processors, long gigabytes, int expected)
    {
        Assert.Equal(expected, JavaScriptRenderer.SlotsFor(concurrency, processors, gigabytes * 1024 * 1024 * 1024));
    }

    /// <summary>
    /// The site's sign-in goes to the site and its www twin, on any port, and a host's own sign-in to that host. No
    /// other host is offered a password.
    /// </summary>
    [Fact]
    public void ASignInIsForTheSiteAndItsWwwTwinAndNoOtherHost()
    {
        var options = new CrawlOptions
        {
            StartUrl = "https://example.com/",
            HttpLogins =
            [
                new SiteLogin { Username = "reader", Password = "let-me-in" },
                new SiteLogin { Host = "staging.example.net", Username = "tester", Password = "other" }
            ]
        };

        Assert.Equal("reader", JavaScriptRenderer.LoginFor(options, new Uri("https://example.com/page"))?.Username);
        Assert.Equal("reader", JavaScriptRenderer.LoginFor(options, new Uri("https://www.example.com/page"))?.Username);
        Assert.Equal("tester", JavaScriptRenderer.LoginFor(options, new Uri("https://staging.example.net/app.js"))?.Username);
        Assert.Null(JavaScriptRenderer.LoginFor(options, new Uri("https://cdn.example.org/app.js")));
        Assert.Null(JavaScriptRenderer.LoginFor(options, new Uri("https://shop.example.com/")));
        Assert.Null(JavaScriptRenderer.LoginFor(options, new Uri("https://www.staging.example.net/")));
    }

    [Fact]
    public async Task WhatAShadowRootShowsIsInTheRenderedHtml()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var html = await HtmlAsync(renderer, site.Url("/shadow"));

        Assert.NotNull(html);
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h2>In the shadow</h2>", html, StringComparison.Ordinal);
        Assert.Contains("/found-in-shadow", html, StringComparison.Ordinal);
        Assert.Contains("Slotted name", html, StringComparison.Ordinal);
        Assert.Contains("Fallback words", html, StringComparison.Ordinal);
        Assert.Contains("<p>Nested shadow</p>", html, StringComparison.Ordinal);

        // A child the shadow root gives no slot is not on the page a reader sees.
        Assert.DoesNotContain("never shown", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentThatWaitsToBeScrolledToIsRendered()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var html = await HtmlAsync(renderer, site.Url("/below"));

        Assert.NotNull(html);
        Assert.Contains("/found-below", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServiceWorkerIsNotStartedSoNoRequestGoesRoundTheCrawlsRules()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        Assert.NotNull(await HtmlAsync(renderer, site.Url("/worker")));

        Assert.Contains("/after-register", _requests);
        Assert.DoesNotContain("/sw.js", _requests);
        Assert.DoesNotContain("/from-worker", _requests);
    }

    /// <summary>
    /// A crawl held to one request a second holds the page's own requests back too. That wait is the crawl's, not the
    /// page's, so it doesn't use up the time the page is given to settle.
    /// </summary>
    [Fact]
    public async Task RequestsTheCrawlsPaceHoldsBackDoNotCutTheRenderShort()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri, requestsPerSecond: 1);

        var html = await HtmlAsync(renderer, site.Url("/chained"));

        Assert.NotNull(html);
        Assert.Contains("<h1>Chained</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatRobotsTxtKeepsCrawlersOutOfIsNotAskedForByAPage()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);
        renderer.ReadRobotsTxt = _ => Task.FromResult(RobotsTxtParser.Parse("User-agent: *\nDisallow: /kept-out/\n", "ShoutingIguana"));

        var html = await HtmlAsync(renderer, site.Url("/asks"));

        Assert.NotNull(html);
        Assert.Contains("<h1>Asked fulfilled rejected</h1>", html, StringComparison.Ordinal);
        Assert.Contains("/data", _requests);
        Assert.DoesNotContain("/kept-out/data", _requests);
    }

    /// <summary>
    /// The plain fetch read the page. A firewall, or a sign-in the browser wasn't given, can answer the browser with
    /// something else: that answer is not the page, so the HTML that was sent is kept.
    /// </summary>
    [Fact]
    public async Task APageTheBrowserIsRefusedIsKeptAsSent()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        Assert.Null(await HtmlAsync(renderer, site.Url("/refused")));
        Assert.Null(await HtmlAsync(renderer, site.Url("/members")));
    }

    [Fact]
    public async Task TheSitesSignInIsSentByTheBrowserToo()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri, edit: options => options with
        {
            HttpLogins = [new SiteLogin { Username = "reader", Password = "let-me-in" }]
        });

        var html = await HtmlAsync(renderer, site.Url("/members"));

        Assert.NotNull(html);
        Assert.Contains("<h1>Members</h1>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// On Linux the browser needs system libraries. Playwright says which in a box drawn for a terminal, with a command
    /// from a developer's build folder. The crawl's alert names the libraries and the command that installs them.
    /// </summary>
    [Fact]
    public void MissingSystemLibrariesAreNamedWithTheCommandThatInstallsThem()
    {
        const string playwright = """

            ╔══════════════════════════════════════════════════════╗
            ║ Host system is missing dependencies to run browsers. ║
            ║ Please install them with the following command:      ║
            ║                                                      ║
            ║     pwsh bin/Debug/netX/playwright.ps1 install-deps  ║
            ║                                                      ║
            ║ Alternatively, use apt:                              ║
            ║     apt-get install libnspr4\                        ║
            ║         libnss3\                                     ║
            ║         libatk-bridge2.0-0\                          ║
            ║         libasound2                                   ║
            ║                                                      ║
            ║ <3 Playwright Team                                   ║
            ╚══════════════════════════════════════════════════════╝
            """;

        Assert.Equal(
            "The browser needs system libraries this computer doesn't have. Install them, then start the crawl again: " +
            "sudo apt-get install libnspr4 libnss3 libatk-bridge2.0-0 libasound2",
            JavaScriptRenderer.PlainReason(playwright));
        Assert.Equal("Something else went wrong.", JavaScriptRenderer.PlainReason("Something else went wrong."));
    }

    /// <summary>
    /// The browser can't start: its driver is missing, as in an app packaged without it. The crawl is told once, as an
    /// error, what it asked of the browser and is not getting, and why. Pages are read as sent, a blocked host stays
    /// blocked, and a sign-in fails the crawl. Nothing is passed off as rendered.
    /// </summary>
    [Fact]
    public async Task ABrowserThatCannotStartIsAnErrorThatSaysWhatTheCrawlIsNotGetting()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer, edit: options => options with
        {
            Rendering = JavaScriptRendering.JavaScript,
            BrowserFallback = true,
            // The computer's own browser needs no driver. Without it, the headless one is all the fallback has.
            UseInstalledBrowser = false
        });
        renderer.CreatePlaywright = () => throw new PlaywrightException("Driver not found: /.playwright/node/linux-x64/node");

        Assert.Null(await HtmlAsync(renderer, site.Url("/plain")));
        Assert.Null(await HtmlAsync(renderer, site.Url("/router")));
        Assert.Null(await renderer.LoadAsync(site.Url("/plain"), "127.0.0.1", CancellationToken.None));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.SignInAsync(
            new FormLogin { Url = site.Url("/plain").AbsoluteUri, Username = "reader", Password = "let-me-in" }, CancellationToken.None));
        Assert.Contains("browser", refused.Message, StringComparison.OrdinalIgnoreCase);

        var notice = Assert.Single(observer.Seen);
        Assert.Equal(CrawlNoticeLevel.Error, notice.Level);
        Assert.Equal("javascript-rendering", notice.Code);
        Assert.Contains("No page of this crawl is rendered", notice.Message, StringComparison.Ordinal);
        Assert.Contains("a host that blocks plain HTTP stays blocked", notice.Message, StringComparison.Ordinal);
        Assert.Contains("Driver not found", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrowserThatCannotStartForAFallbackOnlySaysSo()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer, edit: options => options with
        {
            Rendering = JavaScriptRendering.Off,
            BrowserFallback = true,
            // The computer's own browser needs no driver. Without it, the headless one is all the fallback has.
            UseInstalledBrowser = false
        });
        renderer.CreatePlaywright = () => throw new PlaywrightException("Driver not found");

        Assert.Null(await renderer.LoadAsync(site.Url("/plain"), "127.0.0.1", CancellationToken.None));

        var notice = Assert.Single(observer.Seen);
        Assert.Equal(CrawlNoticeLevel.Error, notice.Level);
        Assert.DoesNotContain("rendered", notice.Message, StringComparison.Ordinal);
        Assert.Contains("A host that blocks plain HTTP stays blocked", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrowserThatDiedIsStartedAgain()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer);
        Assert.NotNull(await HtmlAsync(renderer, site.Url("/plain")));

        var browser = (IBrowser)typeof(JavaScriptRenderer).GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        await browser.CloseAsync();

        var html = await HtmlAsync(renderer, site.Url("/plain"));
        Assert.NotNull(html);
        Assert.Contains("<h1>Plain</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(observer.Seen, notice => notice.Level == CrawlNoticeLevel.Warning);
    }

    private static async Task<string?> HtmlAsync(JavaScriptRenderer renderer, Uri uri) =>
        (await renderer.RenderAsync(uri, CancellationToken.None)).Html;

    private static JavaScriptRenderer Renderer(Uri start, ICrawlObserver? observer = null, double requestsPerSecond = 0,
        Func<CrawlOptions, CrawlOptions>? edit = null)
    {
        var options = new CrawlOptions { StartUrl = start.AbsoluteUri, MaxRequestsPerSecond = requestsPerSecond };
        options = edit?.Invoke(options) ?? options;
        var scope = new CrawlScope(start, includeSubdomains: false, stayInStartFolder: false);
        var interval = requestsPerSecond > 0 ? TimeSpan.FromSeconds(1 / requestsPerSecond) : TimeSpan.Zero;
        var gates = new HostGateRegistry(scope, _ => new HostGate(options.Concurrency, interval, TimeProvider.System));
        return new JavaScriptRenderer(options, UserAgents.Resolve(options.UserAgent, options.CustomUserAgent), scope,
            new PrivateNetworkGuard(options.StartUrl), gates, observer ?? NullCrawlObserver.Instance, TimeProvider.System,
            NullLogger<JavaScriptRenderer>.Instance);
    }

    private static string Page(string body) =>
        $"<!DOCTYPE html><html><head><title>Page</title></head><body><p>{WORDS}</p>{body}</body></html>";

    private async Task ServeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        _requests.Enqueue(path);
        if (path is "/bundle.js" or "/fresh.js")
        {
            context.Response.ContentType = "text/javascript";
            if (path == "/fresh.js")
            {
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync("window.fresh = true;");
                return;
            }

            await context.Response.WriteAsync("fetch('/data').then(() => { document.getElementById('app').innerHTML = '<h1>Bun' + 'dled</h1>'; });");
            return;
        }

        if (path == "/site.css")
        {
            context.Response.ContentType = "text/css";
            await context.Response.WriteAsync("body { margin: 0; }");
            return;
        }

        if (path == "/sw.js")
        {
            context.Response.ContentType = "text/javascript";
            await context.Response.WriteAsync("fetch('/from-worker');");
            return;
        }

        if (path is "/data" or "/after-register" or "/from-worker" or "/kept-out/data" or "/next-route" or "/next-page.js")
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{}");
            return;
        }

        if (path == "/refused")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!DOCTYPE html><html><head><title>Refused</title></head><body><h1>Refused</h1></body></html>");
            return;
        }

        // "reader:let-me-in"
        if (path == "/members" && context.Request.Headers.Authorization != "Basic cmVhZGVyOmxldC1tZS1pbg==")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Members\"";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!DOCTYPE html><html><head><title>Sign in</title></head><body><h1>Sign in</h1></body></html>");
            return;
        }

        if (!PAGES.TryGetValue(path, out var html))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }

    private sealed class Notices : ICrawlObserver
    {
        public ConcurrentQueue<CrawlNotice> Seen { get; } = new();

        public void OnProgress(CrawlProgress progress)
        {
        }

        public void OnNotice(CrawlNotice notice) => Seen.Enqueue(notice);
    }
}
