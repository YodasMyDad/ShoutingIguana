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

        var html = await renderer.RenderAsync(site.Url("/router"), CancellationToken.None);

        Assert.NotNull(html);
        Assert.Contains("<h1>Routed</h1>", html, StringComparison.Ordinal);
        Assert.Contains("/found-by-router", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageItsScriptLeavesIsKeptAsSentAndTheCrawlIsToldOnce()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer);

        Assert.Null(await renderer.RenderAsync(site.Url("/leaves"), CancellationToken.None));
        Assert.Null(await renderer.RenderAsync(site.Url("/leaves"), CancellationToken.None));

        var notice = Assert.Single(observer.Seen, notice => notice.Code == "javascript-redirect");
        Assert.Equal(CrawlNoticeLevel.Warning, notice.Level);
        Assert.Contains(site.Url("/leaves").AbsoluteUri, notice.Message, StringComparison.Ordinal);
        Assert.Contains(site.Url("/landed").AbsoluteUri, notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatAShadowRootShowsIsInTheRenderedHtml()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        var html = await renderer.RenderAsync(site.Url("/shadow"), CancellationToken.None);

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

        var html = await renderer.RenderAsync(site.Url("/below"), CancellationToken.None);

        Assert.NotNull(html);
        Assert.Contains("/found-below", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServiceWorkerIsNotStartedSoNoRequestGoesRoundTheCrawlsRules()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);

        Assert.NotNull(await renderer.RenderAsync(site.Url("/worker"), CancellationToken.None));

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

        var html = await renderer.RenderAsync(site.Url("/chained"), CancellationToken.None);

        Assert.NotNull(html);
        Assert.Contains("<h1>Chained</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatRobotsTxtKeepsCrawlersOutOfIsNotAskedForByAPage()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri);
        renderer.ReadRobotsTxt = _ => Task.FromResult(RobotsTxtParser.Parse("User-agent: *\nDisallow: /kept-out/\n", "ShoutingIguana"));

        var html = await renderer.RenderAsync(site.Url("/asks"), CancellationToken.None);

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

        Assert.Null(await renderer.RenderAsync(site.Url("/refused"), CancellationToken.None));
        Assert.Null(await renderer.RenderAsync(site.Url("/members"), CancellationToken.None));
    }

    [Fact]
    public async Task TheSitesSignInIsSentByTheBrowserToo()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        await using var renderer = Renderer(site.BaseUri, edit: options => options with
        {
            HttpLogins = [new SiteLogin { Username = "reader", Password = "let-me-in" }]
        });

        var html = await renderer.RenderAsync(site.Url("/members"), CancellationToken.None);

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

    [Fact]
    public async Task ABrowserThatDiedIsStartedAgain()
    {
        await using var site = await LoopbackServer.StartAsync(ServeAsync);
        var observer = new Notices();
        await using var renderer = Renderer(site.BaseUri, observer);
        Assert.NotNull(await renderer.RenderAsync(site.Url("/plain"), CancellationToken.None));

        var browser = (IBrowser)typeof(JavaScriptRenderer).GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        await browser.CloseAsync();

        var html = await renderer.RenderAsync(site.Url("/plain"), CancellationToken.None);
        Assert.NotNull(html);
        Assert.Contains("<h1>Plain</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(observer.Seen, notice => notice.Level == CrawlNoticeLevel.Warning);
    }

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
        if (path == "/sw.js")
        {
            context.Response.ContentType = "text/javascript";
            await context.Response.WriteAsync("fetch('/from-worker');");
            return;
        }

        if (path is "/data" or "/after-register" or "/from-worker" or "/kept-out/data")
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
