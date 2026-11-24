using System.Collections.Concurrent;
using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Renders one page in a headless Chromium, after the HTTP fetch, and returns the HTML the page has once its scripts
/// have run. One browser for the crawl, started the first time a page needs it and closed with the crawl. As many
/// pages render at once as the crawl's concurrency, up to what the machine has processors and memory for
/// (<see cref="SlotsFor"/>). Each request the browser makes takes one slot on that host's gate, the same politeness
/// as a fetch, and images, fonts and media are not requested. A script or a stylesheet one page was sent is kept
/// for the crawl (<see cref="RenderCache"/>), so the next page is not sent it again: that takes no slot. A private address is not requested. The returned HTML is what
/// the crawl parses, once; nothing here writes the crawl file.
/// <para>
/// A page is done when its document is parsed and the network has then been quiet, or <see cref="SETTLE_MS"/> have
/// passed. Time a request spends waiting for the crawl's own pace is not the page's: it is not counted against the
/// page's timeout, and the page is not read while one waits. The window is as tall as Google's renderer makes it, so
/// content that loads when it comes into view is there without scrolling. What an open shadow root shows is written
/// into the HTML. A service worker is not started: its requests would go round every rule here.
/// </para>
/// </summary>
internal sealed partial class JavaScriptRenderer(
    CrawlOptions options,
    ResolvedUserAgent userAgent,
    CrawlScope scope,
    PrivateNetworkGuard guard,
    HostGateRegistry gates,
    ICrawlObserver observer,
    TimeProvider timeProvider,
    ILogger<JavaScriptRenderer> logger) : IAsyncDisposable
{
    /// <summary>The most pages rendered at once, whatever the crawl's concurrency and the machine.</summary>
    private const int MAX_RENDER_SLOTS = 8;

    /// <summary>What one page being rendered is allowed for: a renderer process and its share of the browser.</summary>
    private const long BYTES_PER_SLOT = 512L * 1024 * 1024;

    /// <summary>How long to wait, after the document is parsed, for the scripts' own requests to finish.</summary>
    private const int SETTLE_MS = 2_000;

    /// <summary>
    /// How many times the page's timeout a render may take in all, when its requests keep waiting for the crawl's pace.
    /// </summary>
    private const int HELD_PATIENCE = 3;

    private const int VIEWPORT_WIDTH = 1280;

    /// <summary>
    /// Google renders in a window about this tall and does not scroll, so what a page loads as it comes into view
    /// (lazy images, an observer that adds the next block) has loaded in it.
    /// </summary>
    private const int VIEWPORT_HEIGHT = 9_000;

    /// <summary>A browser that keeps stopping is given up on after this many starts.</summary>
    private const int MAX_BROWSER_STARTS = 5;

    /// <summary>
    /// The page's HTML with what each open shadow root shows moved into its host, deepest first, a slot replaced by
    /// what it was given or its own fallback. Null when the page has no shadow root: the page's own HTML is used.
    /// </summary>
    private const string FLATTENED_HTML = """
        () => {
          const hosts = [];
          const collect = (root) => {
            for (const element of root.querySelectorAll('*')) {
              if (element.shadowRoot) {
                hosts.push(element);
                collect(element.shadowRoot);
              }
            }
          };
          collect(document);
          if (hosts.length === 0) return null;
          for (const host of hosts.reverse()) {
            const root = host.shadowRoot;
            for (const slot of [...root.querySelectorAll('slot')]) {
              const assigned = slot.assignedNodes({ flatten: true });
              slot.replaceWith(...(assigned.length > 0 ? assigned : [...slot.childNodes]));
            }
            host.replaceChildren(...root.childNodes);
          }
          const doctype = document.doctype ? '<!DOCTYPE ' + document.doctype.name + '>' : '';
          return doctype + document.documentElement.outerHTML;
        }
        """;

    private readonly bool _startsOnHttps = options.StartUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    private readonly SignatureBook _signatures = new(options.Signatures, timeProvider);
    private readonly List<Cookie> _jar = SeedJar(options);
    private readonly SemaphoreSlim _slots = NewSlots(options.Concurrency);
    private readonly RenderCache _cache = new();
    private long _requestsSent;
    private readonly ConcurrentDictionary<string, long> _sentByKind = new(StringComparer.Ordinal);
    private long _requestsFromCache;
    private long _pagesRendered;
    private readonly SemaphoreSlim _launch = new(1, 1);

    /// <summary>
    /// Reads a host's robots.txt, from the crawl's own cache. Set once, before the first page. A request a page makes
    /// is held to it as a fetch is: a script or an API the site keeps crawlers out of is not asked for.
    /// </summary>
    public Func<Uri, Task<RobotsTxt>>? ReadRobotsTxt { get; set; }

    /// <summary>Starts Playwright's driver. A test gives one that fails, as an app packaged without the driver does.</summary>
    internal Func<Task<IPlaywright>> CreatePlaywright { get; set; } = Playwright.CreateAsync;

    private IPlaywright? _playwright;
    private string? _unavailableReason;
    private IBrowser? _browser;
    private Socks5AuthRelay? _socksRelay;
    private int _unavailable;
    private int _installTried;
    private int _browserStarts;
    private int _scriptRedirectSaid;

    /// <summary>
    /// Pages to render at once: the crawl's concurrency, but no more than half the processors, than a slot for each
    /// <see cref="BYTES_PER_SLOT"/> of memory, or than <see cref="MAX_RENDER_SLOTS"/>. Never fewer than one. A page's
    /// requests still take their turn on each host's gate, so more slots don't ask a host for more at once.
    /// </summary>
    internal static int SlotsFor(int concurrency, int processors, long memoryBytes)
    {
        var machine = Math.Min(processors / 2, (int)Math.Min(memoryBytes / BYTES_PER_SLOT, MAX_RENDER_SLOTS));
        return Math.Clamp(Math.Min(concurrency, machine), 1, MAX_RENDER_SLOTS);
    }

    private static SemaphoreSlim NewSlots(int concurrency)
    {
        var slots = SlotsFor(concurrency, Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        return new SemaphoreSlim(slots, slots);
    }

    /// <summary>How many requests rendered pages made of sites, and how many were answered from the crawl's own cache.</summary>
    internal (long Pages, long Sent, long FromCache, long CacheBytes) Counts =>
        (Interlocked.Read(ref _pagesRendered), Interlocked.Read(ref _requestsSent), Interlocked.Read(ref _requestsFromCache), _cache.Bytes);

    /// <summary>
    /// The HTML the browser has for <paramref name="uri"/>, or none to keep the HTTP response: rendering is off for
    /// the rest of the crawl, the browser couldn't start, or the page didn't load. When the page's script loaded
    /// another page, the result names where it went, and the HTTP response is kept.
    /// </summary>
    public async Task<RenderResult> RenderAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _unavailable) == 1)
        {
            return RenderResult.None;
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        IBrowserContext? context = null;

        // Each document the tab asks for of its own accord. A router that rewrites the address asks for none; a script
        // that leaves the page asks for the next one.
        var documents = new ConcurrentQueue<string>();
        try
        {
            var browser = await BrowserAsync().ConfigureAwait(false);
            if (browser is null)
            {
                return RenderResult.None;
            }

            var started = Stopwatch.StartNew();
            var held = new HeldRequests();
            context = await OpenContextAsync(browser, held).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);
            using var _ = cancellationToken.Register(() => page.CloseAsync());
            page.Request += (_, request) =>
            {
                if (IsMainDocument(request, page) && request.RedirectedFrom is null)
                {
                    documents.Enqueue(request.Url);
                }
            };

            var navigation = page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 0 });
            if (!await InThePagesTimeAsync(navigation, held, cancellationToken, () => LeftFor(documents, uri) is not null).ConfigureAwait(false))
            {
                // A script that leaves before the page has loaded interrupts the load, and pages that send the
                // browser to each other never finish one: where it first went is all there is to know.
                if (LeftFor(documents, uri) is { } gone)
                {
                    return Left(uri, gone);
                }

                logger.LogDebug("Rendering {Url} timed out; keeping the HTTP HTML", uri);
                return RenderResult.None;
            }

            var answer = await navigation.ConfigureAwait(false);
            if (LeftFor(documents, uri) is null && answer is not { Status: >= 200 and < 300 })
            {
                // The plain fetch read this page. A firewall's challenge, or a sign-in page, is not that page rendered.
                logger.LogDebug("The browser was answered {Status} for {Url}; keeping the HTTP HTML", answer?.Status, uri);
                return RenderResult.None;
            }

            await SettleAsync(page, held, cancellationToken).ConfigureAwait(false);

            if (LeftFor(documents, uri) is { } target)
            {
                return Left(uri, target);
            }

            var html = await HtmlAsync(page).ConfigureAwait(false);
            Interlocked.Increment(ref _pagesRendered);
            logger.LogDebug("Rendered {Url} in {ElapsedMs} ms", uri, started.ElapsedMilliseconds);
            return new RenderResult(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A crawl that is stopping closed the page: the URL is not read, rather than kept without its render.
            cancellationToken.ThrowIfCancellationRequested();

            // A script that leaves while the page is still loading interrupts that load.
            if (LeftFor(documents, uri) is { } target)
            {
                return Left(uri, target);
            }

            logger.LogDebug(ex, "Rendering {Url} failed; keeping the HTTP HTML", uri);
            return RenderResult.None;
        }
        finally
        {
            if (context is not null)
            {
                await CloseAsync(context).ConfigureAwait(false);
            }

            _slots.Release();
        }
    }

    /// <summary>
    /// The first other page the tab went to of its own accord, when it did: a script set the location, or wrote a
    /// meta refresh. A page that loaded itself again has not left.
    /// </summary>
    private static Uri? LeftFor(ConcurrentQueue<string> documents, Uri requested)
    {
        foreach (var url in documents.Skip(1))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme is "http" or "https" && !Stayed(url, requested))
            {
                return target;
            }
        }

        return null;
    }

    private RenderResult Left(Uri uri, Uri target)
    {
        logger.LogDebug("Rendered {Url} went on to {Target}; keeping the HTTP HTML and following it", uri, target);
        SayScriptRedirect(uri, target.AbsoluteUri);
        return new RenderResult(null, target);
    }

    public async ValueTask DisposeAsync()
    {
        await _launch.WaitAsync().ConfigureAwait(false);
        try
        {
            var (pages, sent, fromCache, cacheBytes) = Counts;
            if (sent + fromCache > 0)
            {
                logger.LogInformation(
                    "Rendered {Pages} pages: {Sent} requests sent ({Kinds}), {FromCache} answered from the crawl's cache ({CacheBytes} bytes kept)",
                    pages, sent, string.Join(", ", _sentByKind.OrderByDescending(kind => kind.Value).Select(kind => $"{kind.Value} {kind.Key}")), fromCache, cacheBytes);
            }

            await CloseFallbackAsync().ConfigureAwait(false);
            if (_browser is not null)
            {
                await _browser.CloseAsync().ConfigureAwait(false);
            }

            _playwright?.Dispose();
            if (_socksRelay is not null)
            {
                await _socksRelay.DisposeAsync().ConfigureAwait(false);
            }

            _browser = null;
            _playwright = null;
            _socksRelay = null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Closing the rendering browser failed");
        }
        finally
        {
            _launch.Release();
            _launch.Dispose();
            _slots.Dispose();
        }
    }

    /// <summary>
    /// Signs in on <paramref name="login"/> and returns the cookies the page set. Those cookies are also kept for
    /// later browser fetches. Throws when the browser can't run, or the sign-in doesn't get past the form: a crawl
    /// that was told to sign in must not go on and audit the signed-out site.
    /// </summary>
    public async Task<IReadOnlyList<SentCookie>> SignInAsync(FormLogin login, CancellationToken cancellationToken)
    {
        var browser = await BrowserAsync().ConfigureAwait(false);
        if (browser is null)
        {
            throw new InvalidOperationException($"Sign-in needs the browser, and it couldn't start, so the crawl did not run. {_unavailableReason}".TrimEnd());
        }

        if (!Uri.TryCreate(login.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The sign-in page is not an absolute http or https URL.");
        }

            IBrowserContext? context = null;
            try
            {
                context = await OpenContextAsync(browser, new HeldRequests()).ConfigureAwait(false);
                var page = await context.NewPageAsync().ConfigureAwait(false);
                using var _ = cancellationToken.Register(() => page.CloseAsync());
                await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = options.RequestTimeoutSeconds * 1000f
                }).ConfigureAwait(false);

                var seen = (await context.CookiesAsync().ConfigureAwait(false))
                    .Select(cookie => cookie.Name + "=" + cookie.Value)
                    .ToHashSet(StringComparer.Ordinal);
                var userField = login.UsernameField.Length == 0
                    ? "input[type='email'], input[type='text'], input:not([type])"
                    : login.UsernameField;
                var passwordField = login.PasswordField.Length == 0 ? "input[type='password']" : login.PasswordField;
                var submit = login.Submit.Length == 0 ? "button[type='submit'], input[type='submit']" : login.Submit;
                await page.Locator(userField).First.FillAsync(login.Username).ConfigureAwait(false);
                await page.Locator(passwordField).First.FillAsync(login.Password).ConfigureAwait(false);
                await page.Locator(submit).First.ClickAsync().ConfigureAwait(false);
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
                    {
                        Timeout = options.RequestTimeoutSeconds * 1000f
                    }).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }

                List<SentCookie> cookies = [];
                foreach (var cookie in await context.CookiesAsync().ConfigureAwait(false))
                {
                    if (!seen.Contains(cookie.Name + "=" + cookie.Value))
                    {
                        cookies.Add(new SentCookie
                        {
                            Name = cookie.Name,
                            Value = cookie.Value,
                            Domain = cookie.Domain,
                            Path = cookie.Path,
                            Secure = cookie.Secure
                        });
                    }
                }

                if (cookies.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Sign-in didn't get past {uri.AbsoluteUri}. The user name or password was refused, or the form isn't the one this crawl was told about.");
                }

                Remember(cookies);
                return cookies;
            }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Sign-in didn't get past {uri.AbsoluteUri}. {ex.Message}", ex);
        }
        finally
        {
            if (context is not null)
            {
                await context.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Closes a context whose browser may already have gone.</summary>
    private async Task CloseAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Closing a browser context failed");
        }
    }

    /// <summary>Cookies a sign-in set, sent with later browser fetches. Called once, before those fetches.</summary>
    public void Remember(IReadOnlyList<SentCookie> cookies)
    {
        foreach (var cookie in cookies)
        {
            if (cookie.Name.Length == 0 || cookie.Value.Length == 0)
            {
                continue;
            }

            _jar.Add(new Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path.Length == 0 ? "/" : cookie.Path,
                Secure = cookie.Secure
            });
        }
    }

    /// <param name="held">Counts this context's requests while they wait for the crawl's pace.</param>
    private async Task<IBrowserContext> OpenContextAsync(IBrowser browser, HeldRequests held)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = userAgent.Header,
            IgnoreHTTPSErrors = options.TrustedCertificate is not null,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = options.AcceptLanguage },
            JavaScriptEnabled = true,
            ViewportSize = new ViewportSize { Width = VIEWPORT_WIDTH, Height = VIEWPORT_HEIGHT },

            // A service worker's requests are not routed, so they would miss the host's gate and the private-address check.
            ServiceWorkers = ServiceWorkerPolicy.Block
        }).ConfigureAwait(false);
        context.SetDefaultTimeout(options.RequestTimeoutSeconds * 1000f);
        await context.RouteAsync("**/*", route => RouteAsync(route, held)).ConfigureAwait(false);
        if (_jar.Count > 0)
        {
            await context.AddCookiesAsync(_jar).ConfigureAwait(false);
        }

        return context;
    }

    /// <summary>
    /// The HTTP sign-in the crawl was given for <paramref name="uri"/>'s host, as the plain fetch reads the same
    /// list: a sign-in with no host, or with the site's, is for the site and its www twin; one with another host is
    /// for that host alone. Null for every other host, which is never offered a password.
    /// </summary>
    internal static SiteLogin? LoginFor(CrawlOptions options, Uri uri)
    {
        if (!Uri.TryCreate(options.StartUrl, UriKind.Absolute, out var start))
        {
            return null;
        }

        var host = UrlNormalizer.GetHost(uri);
        var site = CrawlScope.SiteHost(UrlNormalizer.GetHost(start));
        foreach (var login in options.HttpLogins)
        {
            var named = login.Host.Trim().ToLowerInvariant();
            var forSite = named.Length == 0 || CrawlScope.SiteHost(named) == site;
            if (login.Username.Length > 0 && (forSite ? CrawlScope.SiteHost(host) == site : host == named))
            {
                return login;
            }
        }

        return null;
    }

    private static List<Cookie> SeedJar(CrawlOptions options)
    {
        List<Cookie> jar = [];
        if (!Uri.TryCreate(options.StartUrl, UriKind.Absolute, out var start))
        {
            return jar;
        }

        foreach (var cookie in options.Cookies)
        {
            if (cookie.Name.Length == 0 || cookie.Value.Length == 0)
            {
                continue;
            }

            jar.Add(new Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain.Length == 0 ? start.Host : cookie.Domain,
                Path = cookie.Path.Length == 0 ? "/" : cookie.Path,
                Secure = cookie.Secure
            });
        }

        return jar;
    }

    private async Task<IBrowser?> BrowserAsync()
    {
        if (Volatile.Read(ref _unavailable) == 1)
        {
            return null;
        }

        if (_browser is { IsConnected: true } running)
        {
            return running;
        }

        await _launch.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _unavailable) == 1)
            {
                return null;
            }

            if (_browser is { IsConnected: true } started)
            {
                return started;
            }

            if (_browser is not null)
            {
                // It crashed, or was killed. Its contexts went with it; the pages being read in it keep their HTTP HTML.
                logger.LogWarning("The rendering browser stopped; starting it again");
                await ForgetStoppedBrowserAsync().ConfigureAwait(false);
            }

            if (Interlocked.Increment(ref _browserStarts) > MAX_BROWSER_STARTS)
            {
                return Unavailable($"The browser stopped {MAX_BROWSER_STARTS} times.");
            }

            return await LaunchAsync().ConfigureAwait(false);
        }
        finally
        {
            _launch.Release();
        }
    }

    private async Task ForgetStoppedBrowserAsync()
    {
        foreach (var session in _fallbackHosts.Values)
        {
            session.Context = null;
        }

        try
        {
            await _browser!.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Closing the stopped browser failed");
        }

        _playwright?.Dispose();
        _browser = null;
        _playwright = null;
    }

    private async Task<IBrowser?> LaunchAsync()
    {
        try
        {
            return await StartAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException ex) when (Interlocked.Exchange(ref _installTried, 1) == 0 && MissingBrowser(ex))
        {
            logger.LogInformation("Installing Chromium for JavaScript rendering");
            observer.OnNotice(new CrawlNotice(
                CrawlNoticeLevel.Info,
                "javascript-rendering",
                "Downloading the browser that renders pages. This happens once, and the crawl goes on when it is done."));
            var code = await Task.Run(() => InstallBrowser(shellOnly: true)).ConfigureAwait(false);
            if (code != 0)
            {
                return Unavailable("The browser couldn't be downloaded. Check the connection, then start the crawl again.");
            }

            try
            {
                return await StartAsync().ConfigureAwait(false);
            }
            catch (Exception retry) when (retry is not OperationCanceledException)
            {
                return Unavailable(retry.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(ex.Message);
        }
    }

    /// <summary>
    /// Downloads the headless Chromium this version of Playwright drives, and nothing else. The windowed browser is
    /// only downloaded for a computer with no Chrome or Edge, when a host has to be read in one. The installer writes its progress to standard output, which the sidecar keeps for its ready line, so it is
    /// sent to standard error while the download runs.
    /// </summary>
    private static int InstallBrowser(bool shellOnly)
    {
        var standardOutput = Console.Out;
        Console.SetOut(Console.Error);
        try
        {
            return Program.Main(shellOnly ? ["install", "--only-shell", "chromium"] : ["install", "chromium"]);
        }
        finally
        {
            Console.SetOut(standardOutput);
        }
    }

    private async Task<IBrowser> StartAsync()
    {
        MakeDriverRunnable();
        _playwright?.Dispose();
        _playwright = await CreatePlaywright().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Proxy = BrowserProxy(),
            // Chromium sends loopback around a proxy unless told not to. The synthetic site, and a crawl of one, is there.
            Args = options.ProxyUrl is null ? null : ["--proxy-bypass-list=<-loopback>"]
        }).ConfigureAwait(false);
        return _browser;
    }

    /// <summary>
    /// Playwright's driver is a node executable beside the program. The installed app unpacks it from its single file,
    /// which on macOS and Linux can leave it without the right to run.
    /// </summary>
    private void MakeDriverRunnable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, ".playwright", "node");
        if (!Directory.Exists(folder))
        {
            return;
        }

        const UnixFileMode RUN = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        foreach (var node in Directory.EnumerateFiles(folder, "node", SearchOption.AllDirectories))
        {
            try
            {
                var mode = File.GetUnixFileMode(node);
                if ((mode & UnixFileMode.UserExecute) == 0)
                {
                    File.SetUnixFileMode(node, mode | RUN);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Couldn't make {Path} runnable", node);
            }
        }
    }

    /// <summary>
    /// Waits for <paramref name="work"/> for as long as one request may take, counting only the time in which none of
    /// the page's requests is waiting for the crawl's own pace. False when that time, or
    /// <see cref="HELD_PATIENCE"/> times it in all, is used up, or when <paramref name="giveUp"/> says there is no
    /// point in waiting: the caller then closes the page.
    /// </summary>
    private async Task<bool> InThePagesTimeAsync(Task work, HeldRequests held, CancellationToken cancellationToken, Func<bool>? giveUp = null)
    {
        var budget = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        var spent = TimeSpan.Zero;
        var watch = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(100, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var now = watch.Elapsed;
            if (!held.Any)
            {
                spent += now - last;
            }

            last = now;
            if (spent >= budget || now >= budget * HELD_PATIENCE || (!work.IsCompleted && giveUp?.Invoke() == true))
            {
                // Whatever it throws once the page is closed has been answered here.
                _ = work.ContinueWith(static done => done.Exception, TaskScheduler.Default);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The scripts' own requests get <see cref="SETTLE_MS"/> to finish. A page still busy after that is read as it
    /// stands, unless the crawl's own pace held one of its requests back in that time: then the time was not the
    /// page's, and it gets it again, up to <see cref="HELD_PATIENCE"/> times the request timeout.
    /// </summary>
    private async Task SettleAsync(IPage page, HeldRequests held, CancellationToken cancellationToken)
    {
        var limit = TimeSpan.FromSeconds(options.RequestTimeoutSeconds * HELD_PATIENCE);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waits = held.Waits;
            if (await IsQuietAsync(page).ConfigureAwait(false) || watch.Elapsed >= limit || (held.Waits == waits && !held.Any))
            {
                return;
            }
        }
    }

    /// <summary>Whether the page's network went quiet within <see cref="SETTLE_MS"/>.</summary>
    private static async Task<bool> IsQuietAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = SETTLE_MS })
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // The document is parsed and its scripts have run. Later requests can keep the network busy.
            return false;
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    /// <summary>The page as it stands, with what its shadow roots show written into it.</summary>
    private async Task<string> HtmlAsync(IPage page)
    {
        try
        {
            if (await page.EvaluateAsync<string?>(FLATTENED_HTML).ConfigureAwait(false) is { } flattened)
            {
                return flattened;
            }
        }
        catch (PlaywrightException ex)
        {
            // A page that broke the DOM methods this uses still has its HTML.
            logger.LogDebug(ex, "Reading the shadow roots of {Url} failed", page.Url);
        }

        return await page.ContentAsync().ConfigureAwait(false);
    }

    private static bool IsMainDocument(IRequest request, IPage page)
    {
        try
        {
            return request.IsNavigationRequest && request.Frame == page.MainFrame;
        }
        catch (PlaywrightException)
        {
            // A request with no frame is not the tab's document.
            return false;
        }
    }

    /// <summary>Said once a crawl: a page's script sent the browser on, and the crawl follows it.</summary>
    private void SayScriptRedirect(Uri uri, string final)
    {
        if (Interlocked.Exchange(ref _scriptRedirectSaid, 1) == 1)
        {
            return;
        }

        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Info,
            "javascript-redirect",
            $"{uri.AbsoluteUri} sends the browser on to {final} with a script. The crawl follows that as it follows a redirect. " +
            "Search engines follow a script's redirect only once they have rendered the page, so a redirect the server sends is surer.")
        {
            Data = new CrawlNoticeData(Url: uri.AbsoluteUri)
        });
    }

    /// <summary>
    /// Why the browser couldn't start, for the crawl's alert. Playwright reports missing Linux libraries in a box drawn
    /// for a terminal, with a command from a build folder: that becomes the libraries and the command that installs them.
    /// </summary>
    internal static string PlainReason(string why)
    {
        if (!why.Contains("missing dependencies", StringComparison.OrdinalIgnoreCase))
        {
            return why;
        }

        var libraries = LibraryName().Matches(why).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToList();
        return libraries.Count == 0
            ? "The browser needs system libraries this computer doesn't have."
            : "The browser needs system libraries this computer doesn't have. Install them, then start the crawl again: " +
              "sudo apt-get install " + string.Join(' ', libraries);
    }

    [GeneratedRegex(@"\blib[a-z0-9][a-z0-9.+-]*", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryName();

    /// <summary>
    /// The browser can't be used for the rest of the crawl. Said once, as an error the crawl keeps: what this crawl
    /// asked of the browser and is not getting, and why. A crawl that was to be rendered must not read as one that was.
    /// </summary>
    private IBrowser? Unavailable(string why)
    {
        Volatile.Write(ref _unavailable, 1);
        logger.LogError("The browser can't be used in this crawl: {Reason}", why);
        why = PlainReason(why);
        _unavailableReason = why;
        var renders = options.Rendering != JavaScriptRendering.Off;
        var lost = (renders, options.BrowserFallback) switch
        {
            (true, true) => "No page of this crawl is rendered: every page is read as it was sent, and a host that blocks plain HTTP stays blocked.",
            (true, false) => "No page of this crawl is rendered: every page is read as it was sent.",
            (false, true) => "A host that blocks plain HTTP stays blocked.",
            _ => "Nothing is read in it."
        };
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Error,
            "javascript-rendering",
            $"The browser couldn't start. {lost} {why}"));
        return null;
    }

    /// <summary>
    /// Sends the request on, or not. Images, fonts and media are dropped. A private address is never requested, nor a
    /// name that resolves only to one. Extra headers and a crawler signature go where the HTTP fetch would send them.
    /// </summary>
    private async Task RouteAsync(IRoute route, HeldRequests held)
    {
        try
        {
            var request = route.Request;
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (await guard.RefusesAsync(uri).ConfigureAwait(false) || await ResolvesOnlyPrivatelyAsync(uri).ConfigureAwait(false))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (request.ResourceType is "image" or "media" or "font" or "websocket" || IsAskedAhead(request))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            if (await RobotsTxtRefusesAsync(request, uri, held).ConfigureAwait(false))
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            // A script or stylesheet an earlier page was sent is given again from the crawl's cache. It was allowed
            // above, as every request is, and the site is not asked, so it takes no slot on the host's gate.
            var cacheable = RenderCache.Serves(request.Method, request.ResourceType);
            if (cacheable && _cache.TryGet(uri.AbsoluteUri, out var kept))
            {
                Interlocked.Increment(ref _requestsFromCache);
                await route.FulfillAsync(new RouteFulfillOptions { Status = kept.Status, Headers = kept.Headers, BodyBytes = kept.Body })
                    .ConfigureAwait(false);
                return;
            }

            var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
            var mayCarrySecrets = uri.Scheme == Uri.UriSchemeHttps || !_startsOnHttps;
            if (scope.IsInternal(uri) && mayCarrySecrets)
            {
                foreach (var (name, value) in options.ExtraHeaders)
                {
                    headers[name] = value;
                }
            }

            // HTTP Basic up front, as the plain fetch sends it, to the hosts the sign-in is for and to no other.
            if (mayCarrySecrets && LoginFor(options, uri) is { } login)
            {
                headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.Username}:{login.Password}"));
            }

            if (_signatures.For(uri) is { } signature)
            {
                headers[CrawlSignature.INPUT_HEADER] = signature.SignatureInput;
                headers[CrawlSignature.SIGNATURE_HEADER] = signature.Signature;
                headers[CrawlSignature.AGENT_HEADER] = CrawlSignature.SHOPIFY_AGENT;
            }

            // One slot for this request, held until it has been answered, so a render's script doesn't open a second
            // connection beside the document. The page's own fetch already took its slot and let it go.
            HostGateLease lease;
            var began = held.Begin();
            try
            {
                lease = await gates.For(uri).AcquireAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                held.End(began);
            }

            try
            {
                var fetched = await route.FetchAsync(new RouteFetchOptions { Headers = headers }).ConfigureAwait(false);
                Interlocked.Increment(ref _requestsSent);
                _sentByKind.AddOrUpdate(request.ResourceType, 1, static (_, count) => count + 1);
                lease.ReportStatus(fetched.Status, null, uri);
                if (cacheable && fetched.Status == 200)
                {
                    _cache.Offer(uri.AbsoluteUri, fetched.Status, fetched.Headers, await fetched.BodyAsync().ConfigureAwait(false));
                }

                await route.FulfillAsync(new RouteFulfillOptions { Response = fetched }).ConfigureAwait(false);
            }
            finally
            {
                lease.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "A rendered page's request was not sent: {Url}", route.Request.Url);
            try
            {
                await route.AbortAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The request already finished.
            }
        }
    }

    /// <summary>
    /// Whether robots.txt keeps the crawler from what a page asks for. The page's own document is not asked about: the
    /// crawl already was, before it rendered it. Reading a robots.txt is the crawl's wait, not the page's.
    /// </summary>
    private async Task<bool> RobotsTxtRefusesAsync(IRequest request, Uri uri, HeldRequests held)
    {
        if (!options.RespectRobotsTxt || ReadRobotsTxt is not { } read || IsTopDocument(request))
        {
            return false;
        }

        var began = held.Begin();
        try
        {
            var rules = await read(uri).ConfigureAwait(false);
            return !rules.IsAllowed(uri.PathAndQuery);
        }
        finally
        {
            held.End(began);
        }
    }

    /// <summary>
    /// A request for where a visitor might go next, not for this page: a prefetch link, which the browser marks, or
    /// the Next.js router fetching the routes its links name. The page is the same without them, and a site is
    /// spared most of what a render would otherwise ask of it.
    /// </summary>
    private static bool IsAskedAhead(IRequest request)
    {
        var headers = request.Headers;
        return (headers.TryGetValue("sec-purpose", out var purpose) && purpose.Contains("prefetch", StringComparison.OrdinalIgnoreCase)) ||
               (headers.TryGetValue("purpose", out var older) && older.Contains("prefetch", StringComparison.OrdinalIgnoreCase)) ||
               headers.ContainsKey("next-router-prefetch");
    }

    private static bool IsTopDocument(IRequest request)
    {
        try
        {
            return request.IsNavigationRequest && request.Frame.ParentFrame is null;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>A name whose every address is private, and the crawl started on a public one. A literal was already refused.</summary>
    private async Task<bool> ResolvesOnlyPrivatelyAsync(Uri uri)
    {
        if (PrivateNetworkGuard.IsPrivateLiteral(uri))
        {
            return false;
        }

        try
        {
            await guard.AddressesToConnectToAsync(uri.IdnHost, CancellationToken.None).ConfigureAwait(false);
            return false;
        }
        catch (PrivateNetworkAddressException)
        {
            return true;
        }
    }

    private static bool Stayed(string pageUrl, Uri requested) =>
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var final) &&
        UrlNormalizer.Normalize(final) == UrlNormalizer.Normalize(requested);

    private static bool MissingBrowser(Exception exception) =>
        exception.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Please run the following command", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The proxy Chromium can use. A SOCKS5 proxy with a login is fronted by <see cref="Socks5AuthRelay"/>, because
    /// Chromium won't send the login itself. An http or https proxy, and a SOCKS5 proxy with no login, are given as they are.
    /// </summary>
    private Proxy? BrowserProxy()
    {
        if (options.ProxyUrl is null || !Uri.TryCreate(options.ProxyUrl, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return null;
        }

        if (uri.Scheme == "socks5" && uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(parts[0]);
            var password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            _socksRelay ??= Socks5AuthRelay.Start(uri.IdnHost, uri.Port, user, password);
            return new Proxy { Server = _socksRelay.Address };
        }

        return ProxyOf(options.ProxyUrl);
    }

    private static Proxy? ProxyOf(string? proxyUrl)
    {
        if (proxyUrl is null || !Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var server = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.GetLeftPart(UriPartial.Authority);
        string? username = null;
        string? password = null;
        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return new Proxy { Server = server, Username = username, Password = password };
    }

    /// <summary>How many of a browser context's requests are waiting for the crawl's own pace, not for the site.</summary>
    private sealed class HeldRequests
    {
        /// <summary>A wait shorter than this is a gate that was open.</summary>
        private const int NOTICEABLE_MS = 100;

        private int _count;
        private int _waits;

        /// <summary>A request is waiting now.</summary>
        public bool Any => Volatile.Read(ref _count) > 0;

        /// <summary>How many waits long enough to notice have ended.</summary>
        public int Waits => Volatile.Read(ref _waits);

        public long Begin()
        {
            Interlocked.Increment(ref _count);
            return Stopwatch.GetTimestamp();
        }

        public void End(long began)
        {
            Interlocked.Decrement(ref _count);
            if (Stopwatch.GetElapsedTime(began).TotalMilliseconds >= NOTICEABLE_MS)
            {
                Interlocked.Increment(ref _waits);
            }
        }
    }
}
