using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IPlaywrightService for managing Playwright browsers.
/// Hosts a bounded BrowserContext pool so concurrent workers reuse contexts
/// rather than creating a fresh one per page.
/// </summary>
public class PlaywrightService(
    ILogger<PlaywrightService> logger,
    IAppSettingsService appSettings) : IPlaywrightService, IDisposable
{
    // Scale the pool with the host CPU but cap at 8: Chromium contexts are
    // memory-heavy (~150MB each) and above 8 we saturate typical desktop RAM.
    private static readonly int DefaultPoolSize = Math.Min(8, Math.Max(2, Environment.ProcessorCount));

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private BrowserStatus _status = BrowserStatus.NotInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _disposeState;

    private SemaphoreSlim _poolSlots = new(DefaultPoolSize, DefaultPoolSize);
    private int _poolCapacity = DefaultPoolSize;
    private readonly ConcurrentDictionary<string, ConcurrentBag<IBrowserContext>> _idleContexts = new();
    private readonly ConcurrentDictionary<IPage, PageLease> _leases = new();
    private readonly ConcurrentBag<IBrowserContext> _liveContexts = new();

    private sealed record PageLease(string Key, IBrowserContext Context);

    public bool IsBrowserInstalled { get; private set; }
    public BrowserStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, new BrowserStatusEventArgs(value));
            }
        }
    }

    public event EventHandler<BrowserStatusEventArgs>? StatusChanged;

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_playwright != null) return;

            Status = BrowserStatus.Initializing;
            logger.LogInformation("Initializing Playwright...");

            _playwright = await Playwright.CreateAsync().ConfigureAwait(false);

            if (appSettings.BrowserSettings.IsBrowserInstalled)
            {
                IsBrowserInstalled = await CheckBrowserInstalledAsync().ConfigureAwait(false);

                if (!IsBrowserInstalled)
                {
                    logger.LogWarning("Browser marked as installed in settings but not found. Resetting flag.");
                    appSettings.BrowserSettings.IsBrowserInstalled = false;
                    await appSettings.SaveAsync().ConfigureAwait(false);
                }
            }
            else
            {
                IsBrowserInstalled = await CheckBrowserInstalledAsync().ConfigureAwait(false);

                if (IsBrowserInstalled)
                {
                    logger.LogInformation("Browser found (installed externally). Updating settings.");
                    appSettings.MarkBrowserInstalled();
                    await appSettings.SaveAsync().ConfigureAwait(false);
                }
            }

            if (IsBrowserInstalled)
            {
                Status = BrowserStatus.Ready;
                logger.LogInformation("Playwright initialized successfully. Browser is ready.");
            }
            else
            {
                Status = BrowserStatus.NotInitialized;
                logger.LogWarning("Playwright initialized but browser not installed.");
            }
        }
        catch (Exception ex)
        {
            Status = BrowserStatus.Error;
            logger.LogError(ex, "Failed to initialize Playwright");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task InstallBrowsersAsync(IProgress<string>? progress = null)
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Status = BrowserStatus.Installing;
            logger.LogInformation("Installing Playwright browsers...");
            progress?.Report("Installing Chromium browser...");

            var exitCode = await RunPlaywrightInstallAsync(progress).ConfigureAwait(false);

            if (exitCode == 0)
            {
                IsBrowserInstalled = true;
                Status = BrowserStatus.Ready;
                logger.LogInformation("Browser installation completed successfully");
                progress?.Report("Installation complete");

                appSettings.MarkBrowserInstalled();
                await appSettings.SaveAsync().ConfigureAwait(false);
            }
            else
            {
                Status = BrowserStatus.Error;
                logger.LogError("Browser installation failed with exit code {ExitCode}", exitCode);
                throw new Exception($"Browser installation failed with exit code {exitCode}");
            }
        }
        catch (Exception ex)
        {
            Status = BrowserStatus.Error;
            logger.LogError(ex, "Error during browser installation");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_playwright == null)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (!IsBrowserInstalled)
        {
            throw new InvalidOperationException("Browser not installed. Call InstallBrowsersAsync first.");
        }

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            logger.LogInformation("Launching Chromium browser...");
            _browser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = [
                    "--disable-blink-features=AutomationControlled",
                    "--disable-dev-shm-usage",
                    "--disable-web-security",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-infobars",
                    "--window-position=0,0",
                    "--ignore-certificate-errors",
                    "--ignore-certificate-errors-spki-list"
                ]
            }).ConfigureAwait(false);

            logger.LogInformation("Browser launched successfully");
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void ConfigurePoolSize(int size)
    {
        if (size < 1) size = 1;
        if (Interlocked.CompareExchange(ref _disposeState, 0, 0) == 1) return;

        lock (_initLock)
        {
            if (_poolCapacity == size) return;

            var oldSemaphore = _poolSlots;
            _poolSlots = new SemaphoreSlim(size, size);
            _poolCapacity = size;
            oldSemaphore.Dispose();
            logger.LogInformation("Playwright context pool sized to {Size}", size);
        }
    }

    public async Task<IPage> CreatePageAsync(string userAgent, ProxySettings? proxySettings = null, bool blockNonEssentialResources = true)
    {
        var browser = await GetBrowserAsync().ConfigureAwait(false);

        SemaphoreSlim slots;
        lock (_initLock)
        {
            slots = _poolSlots;
        }

        await slots.WaitAsync().ConfigureAwait(false);

        string key = BuildProxyKey(proxySettings);
        IBrowserContext? context = null;

        try
        {
            var bag = _idleContexts.GetOrAdd(key, _ => new ConcurrentBag<IBrowserContext>());
            while (bag.TryTake(out var candidate))
            {
                if (!IsContextHealthy(candidate))
                {
                    await SafeDisposeContextAsync(candidate).ConfigureAwait(false);
                    continue;
                }
                context = candidate;
                break;
            }

            if (context == null)
            {
                context = await CreateContextAsync(browser, proxySettings).ConfigureAwait(false);
                _liveContexts.Add(context);
            }

            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout(30000);
            page.SetDefaultNavigationTimeout(30000);

            if (blockNonEssentialResources)
            {
                await page.RouteAsync("**/*", async route =>
                {
                    var type = route.Request.ResourceType;
                    if (type is "image" or "media" or "font")
                    {
                        await route.AbortAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await route.ContinueAsync().ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }

            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["User-Agent"] = userAgent,
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["Sec-Fetch-Dest"] = "document",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-Site"] = "none",
                ["Sec-Fetch-User"] = "?1",
                ["Upgrade-Insecure-Requests"] = "1"
            }).ConfigureAwait(false);

            _leases[page] = new PageLease(key, context);
            return page;
        }
        catch
        {
            if (context != null)
            {
                await SafeDisposeContextAsync(context).ConfigureAwait(false);
            }
            slots.Release();
            throw;
        }
    }

    /// <summary>
    /// Closes the page and returns its context to the pool. If the context or browser is
    /// no longer healthy, the context is disposed instead of being reused.
    /// </summary>
    public async Task ClosePageAsync(IPage page)
    {
        if (!_leases.TryRemove(page, out var lease))
        {
            // Unknown page — close it and its context, matching pre-pool behaviour.
            try
            {
                var ctx = page.Context;
                await page.CloseAsync().ConfigureAwait(false);
                await ctx.CloseAsync().ConfigureAwait(false);
                await ctx.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error closing unmanaged page");
            }
            return;
        }

        bool contextHealthy;
        try
        {
            await page.CloseAsync().ConfigureAwait(false);
            contextHealthy = IsContextHealthy(lease.Context);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error closing pooled page; discarding context");
            contextHealthy = false;
        }

        if (contextHealthy)
        {
            var bag = _idleContexts.GetOrAdd(lease.Key, _ => new ConcurrentBag<IBrowserContext>());
            bag.Add(lease.Context);
        }
        else
        {
            await SafeDisposeContextAsync(lease.Context).ConfigureAwait(false);
        }

        SemaphoreSlim slots;
        lock (_initLock)
        {
            slots = _poolSlots;
        }
        try
        {
            slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // Pool resized or service disposed; nothing to release.
        }
        catch (SemaphoreFullException)
        {
            // Defensive: more returns than borrows (e.g. after resize) — ignore.
        }
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 1)
            return;

        try
        {
            foreach (var kvp in _idleContexts)
            {
                while (kvp.Value.TryTake(out var ctx))
                {
                    await SafeDisposeContextAsync(ctx).ConfigureAwait(false);
                }
            }
            _idleContexts.Clear();

            while (_liveContexts.TryTake(out var live))
            {
                await SafeDisposeContextAsync(live).ConfigureAwait(false);
            }

            if (_browser != null)
            {
                await _browser.CloseAsync().ConfigureAwait(false);
                await _browser.DisposeAsync().ConfigureAwait(false);
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;

            _poolSlots.Dispose();
            _initLock.Dispose();

            logger.LogInformation("Playwright resources disposed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error disposing Playwright resources");
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 1)
            return;

        logger.LogWarning("Synchronous Dispose() called - prefer DisposeAsync() for proper cleanup");

        try
        {
            if (_browser != null || _playwright != null || !_idleContexts.IsEmpty || !_liveContexts.IsEmpty)
            {
                var disposeTask = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var kvp in _idleContexts)
                        {
                            while (kvp.Value.TryTake(out var ctx))
                            {
                                await SafeDisposeContextAsync(ctx).ConfigureAwait(false);
                            }
                        }
                        _idleContexts.Clear();

                        while (_liveContexts.TryTake(out var live))
                        {
                            await SafeDisposeContextAsync(live).ConfigureAwait(false);
                        }

                        if (_browser != null)
                        {
                            await _browser.CloseAsync().ConfigureAwait(false);
                            await _browser.DisposeAsync().ConfigureAwait(false);
                            _browser = null;
                        }

                        _playwright?.Dispose();
                        _playwright = null;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error disposing Playwright resources in synchronous Dispose");
                    }
                });

                if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    logger.LogWarning("Playwright synchronous disposal timed out after 5 seconds");
                }
            }

            _poolSlots.Dispose();
            _initLock.Dispose();

            logger.LogInformation("Playwright resources disposed (synchronous path)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in synchronous Dispose");
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(IBrowser browser, ProxySettings? proxySettings)
    {
        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-US",
            TimezoneId = "America/New_York",
            Permissions = new[] { "geolocation", "notifications" },
            ColorScheme = ColorScheme.Light,
            DeviceScaleFactor = 1,
            HasTouch = false,
            IsMobile = false,
            JavaScriptEnabled = true
        };

        if (proxySettings is { Enabled: true } && !string.IsNullOrWhiteSpace(proxySettings.Server))
        {
            logger.LogInformation("Creating browser context with proxy: {ProxyUrl}", proxySettings.GetRedactedProxyUrl());

            contextOptions.Proxy = new Proxy
            {
                Server = proxySettings.GetProxyUrl()
            };

            if (proxySettings.RequiresAuthentication && !string.IsNullOrWhiteSpace(proxySettings.Username))
            {
                contextOptions.Proxy.Username = proxySettings.Username;
                contextOptions.Proxy.Password = proxySettings.GetPassword();
            }

            if (proxySettings.BypassList.Count > 0)
            {
                contextOptions.Proxy.Bypass = string.Join(",", proxySettings.BypassList);
            }
        }

        var context = await browser.NewContextAsync(contextOptions).ConfigureAwait(false);

        await context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => false });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            Object.defineProperty(navigator, 'mimeTypes', { get: () => [1, 2, 3, 4] });
            window.chrome = { runtime: {} };
            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications' ?
                    Promise.resolve({ state: Notification.permission }) :
                    originalQuery(parameters)
            );
        ").ConfigureAwait(false);

        return context;
    }

    private async Task SafeDisposeContextAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing browser context");
        }

        try
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing browser context");
        }
    }

    private bool IsContextHealthy(IBrowserContext context)
    {
        if (_browser == null || !_browser.IsConnected) return false;
        try
        {
            return context.Browser is { IsConnected: true };
        }
        catch
        {
            return false;
        }
    }

    private static string BuildProxyKey(ProxySettings? proxySettings)
    {
        if (proxySettings is not { Enabled: true } || string.IsNullOrWhiteSpace(proxySettings.Server))
        {
            return "::direct::";
        }
        var bypass = proxySettings.BypassList.Count > 0
            ? string.Join(",", proxySettings.BypassList)
            : "";
        return $"{proxySettings.Type}|{proxySettings.GetProxyUrl()}|{proxySettings.Username}|{bypass}";
    }

    private async Task<bool> CheckBrowserInstalledAsync()
    {
        try
        {
            var testBrowser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = 5000
            }).ConfigureAwait(false);

            await testBrowser.CloseAsync().ConfigureAwait(false);
            await testBrowser.DisposeAsync().ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> RunPlaywrightInstallAsync(IProgress<string>? progress)
    {
        return await Task.Run(() =>
        {
            try
            {
                progress?.Report("Installing Chromium browser...");

                var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

                if (exitCode == 0)
                {
                    progress?.Report("Chromium installation completed successfully");
                }
                else
                {
                    progress?.Report($"Installation failed with exit code {exitCode}");
                }

                return exitCode;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Playwright installation");
                progress?.Report($"Error: {ex.Message}");
                return -1;
            }
        }).ConfigureAwait(false);
    }
}
