using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Renders one page in a headless Chromium, after the HTTP fetch, and returns the HTML the page has once its scripts
/// have run. One browser for the crawl, started the first time a page needs it and closed with the crawl. Two pages
/// render at once. Each request the browser makes takes one slot on that host's gate, the same politeness as a
/// fetch, and images, fonts and media are not requested. A private address is not requested. The returned HTML is what
/// the crawl parses, once; nothing here writes the crawl file.
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
    /// <summary>Pages rendered at once. Each one is a browser, so this stays small whatever the crawl's concurrency.</summary>
    private const int RENDER_SLOTS = 2;

    /// <summary>How long to wait, after the document is parsed, for the scripts' own requests to finish.</summary>
    private const int SETTLE_MS = 2_000;

    private readonly bool _startsOnHttps = options.StartUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    private readonly SignatureBook _signatures = new(options.Signatures, timeProvider);
    private readonly List<Cookie> _jar = SeedJar(options);
    private readonly SemaphoreSlim _slots = new(RENDER_SLOTS, RENDER_SLOTS);
    private readonly SemaphoreSlim _launch = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Socks5AuthRelay? _socksRelay;
    private int _unavailable;
    private int _installTried;

    /// <summary>
    /// The HTML the browser has for <paramref name="uri"/>, or null to keep the HTTP response: rendering is off for
    /// the rest of the crawl, the browser couldn't start, or the page navigated away.
    /// </summary>
    public async Task<string?> RenderAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _unavailable) == 1)
        {
            return null;
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        IBrowserContext? context = null;
        try
        {
            var browser = await BrowserAsync().ConfigureAwait(false);
            if (browser is null)
            {
                return null;
            }

            var started = Stopwatch.StartNew();
            context = await OpenContextAsync(browser).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);
            using var _ = cancellationToken.Register(() => page.CloseAsync());
            await page.GotoAsync(uri.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.RequestTimeoutSeconds * 1000f
            }).ConfigureAwait(false);

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = SETTLE_MS })
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The document is parsed and its scripts have run. Later requests can keep the network busy.
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            {
            }

            if (!Stayed(page.Url, uri))
            {
                logger.LogDebug("Rendered {Url} navigated to {Final}; keeping the HTTP HTML", uri, page.Url);
                return null;
            }

            var html = await page.ContentAsync().ConfigureAwait(false);
            logger.LogDebug("Rendered {Url} in {ElapsedMs} ms", uri, started.ElapsedMilliseconds);
            return html;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Rendering {Url} failed; keeping the HTTP HTML", uri);
            return null;
        }
        finally
        {
            if (context is not null)
            {
                await context.CloseAsync().ConfigureAwait(false);
            }

            _slots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _launch.WaitAsync().ConfigureAwait(false);
        try
        {
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
            throw new InvalidOperationException("Sign-in needs the browser, and it couldn't start, so the crawl did not run.");
        }

        if (!Uri.TryCreate(login.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The sign-in page is not an absolute http or https URL.");
        }

            IBrowserContext? context = null;
            try
            {
                context = await OpenContextAsync(browser).ConfigureAwait(false);
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

    private async Task<IBrowserContext> OpenContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = userAgent.Header,
            IgnoreHTTPSErrors = options.TrustedCertificate is not null,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Accept-Language"] = options.AcceptLanguage },
            JavaScriptEnabled = true
        }).ConfigureAwait(false);
        context.SetDefaultTimeout(options.RequestTimeoutSeconds * 1000f);
        await context.RouteAsync("**/*", RouteAsync).ConfigureAwait(false);
        if (_jar.Count > 0)
        {
            await context.AddCookiesAsync(_jar).ConfigureAwait(false);
        }

        return context;
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

        if (_browser is not null)
        {
            return _browser;
        }

        await _launch.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null || Volatile.Read(ref _unavailable) == 1)
            {
                return _browser;
            }

            return await LaunchAsync().ConfigureAwait(false);
        }
        finally
        {
            _launch.Release();
        }
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
            var code = await Task.Run(() => Program.Main(["install", "chromium"])).ConfigureAwait(false);
            if (code != 0)
            {
                return Unavailable("Chromium couldn't be installed.");
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

    private async Task<IBrowser> StartAsync()
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Proxy = BrowserProxy(),
            // Chromium sends loopback around a proxy unless told not to. The synthetic site, and a crawl of one, is there.
            Args = options.ProxyUrl is null ? null : ["--proxy-bypass-list=<-loopback>"]
        }).ConfigureAwait(false);
        return _browser;
    }

    private IBrowser? Unavailable(string why)
    {
        Volatile.Write(ref _unavailable, 1);
        logger.LogWarning("JavaScript rendering is off for this crawl: {Reason}", why);
        observer.OnNotice(new CrawlNotice(
            CrawlNoticeLevel.Warning,
            "javascript-rendering",
            "JavaScript rendering couldn't start, so pages are read as they were sent. " + why));
        return null;
    }

    /// <summary>
    /// Sends the request on, or not. Images, fonts and media are dropped. A private address is never requested, nor a
    /// name that resolves only to one. Extra headers and a crawler signature go where the HTTP fetch would send them.
    /// </summary>
    private async Task RouteAsync(IRoute route)
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

            if (request.ResourceType is "image" or "media" or "font" or "websocket")
            {
                await route.AbortAsync().ConfigureAwait(false);
                return;
            }

            var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
            if (scope.IsInternal(uri) && (uri.Scheme == Uri.UriSchemeHttps || !_startsOnHttps))
            {
                foreach (var (name, value) in options.ExtraHeaders)
                {
                    headers[name] = value;
                }
            }

            if (_signatures.For(uri) is { } signature)
            {
                headers[CrawlSignature.INPUT_HEADER] = signature.SignatureInput;
                headers[CrawlSignature.SIGNATURE_HEADER] = signature.Signature;
                headers[CrawlSignature.AGENT_HEADER] = CrawlSignature.SHOPIFY_AGENT;
            }

            // One slot for this request, held until it has been answered, so a render's script doesn't open a second
            // connection beside the document. The page's own fetch already took its slot and let it go.
            var lease = await gates.For(uri).AcquireAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var fetched = await route.FetchAsync(new RouteFetchOptions { Headers = headers }).ConfigureAwait(false);
                lease.ReportStatus(fetched.Status, null, uri);
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
}
