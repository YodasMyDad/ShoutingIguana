using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of IPlaywrightService for managing Playwright browsers.
/// </summary>
public class PlaywrightService(
    ILogger<PlaywrightService> logger,
    IAppSettingsService appSettings) : IPlaywrightService, IDisposable
{
    private readonly ILogger<PlaywrightService> _logger = logger;
    private readonly IAppSettingsService _appSettings = appSettings;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private BrowserStatus _status = BrowserStatus.NotInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private int _disposeState = 0; // 0 = not disposed, 1 = disposed

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
        await _initLock.WaitAsync();
        try
        {
            if (_playwright != null) return;

            Status = BrowserStatus.Initializing;
            _logger.LogInformation("Initializing Playwright...");

            _playwright = await Playwright.CreateAsync();
            
            // Check settings first to avoid unnecessary browser launch test
            if (_appSettings.BrowserSettings.IsBrowserInstalled)
            {
                // Trust the settings, but verify once
                IsBrowserInstalled = await CheckBrowserInstalledAsync();
                
                if (!IsBrowserInstalled)
                {
                    // Settings were wrong, update them
                    _logger.LogWarning("Browser marked as installed in settings but not found. Resetting flag.");
                    _appSettings.BrowserSettings.IsBrowserInstalled = false;
                    await _appSettings.SaveAsync();
                }
            }
            else
            {
                // Not installed according to settings, do a quick check
                IsBrowserInstalled = await CheckBrowserInstalledAsync();
                
                if (IsBrowserInstalled)
                {
                    // Browser was installed externally, update settings
                    _logger.LogInformation("Browser found (installed externally). Updating settings.");
                    _appSettings.MarkBrowserInstalled();
                    await _appSettings.SaveAsync();
                }
            }
            
            if (IsBrowserInstalled)
            {
                Status = BrowserStatus.Ready;
                _logger.LogInformation("Playwright initialized successfully. Browser is ready.");
            }
            else
            {
                Status = BrowserStatus.NotInitialized;
                _logger.LogWarning("Playwright initialized but browser not installed.");
            }
        }
        catch (Exception ex)
        {
            Status = BrowserStatus.Error;
            _logger.LogError(ex, "Failed to initialize Playwright");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task InstallBrowsersAsync(IProgress<string>? progress = null)
    {
        await _initLock.WaitAsync();
        try
        {
            Status = BrowserStatus.Installing;
            _logger.LogInformation("Installing Playwright browsers...");
            progress?.Report("Installing Chromium browser...");

            // Run playwright install chromium
            var exitCode = await RunPlaywrightInstallAsync(progress);

            if (exitCode == 0)
            {
                IsBrowserInstalled = true;
                Status = BrowserStatus.Ready;
                _logger.LogInformation("Browser installation completed successfully");
                progress?.Report("Installation complete");
                
                // Persist installation state
                _appSettings.MarkBrowserInstalled();
                await _appSettings.SaveAsync();
            }
            else
            {
                Status = BrowserStatus.Error;
                _logger.LogError("Browser installation failed with exit code {ExitCode}", exitCode);
                throw new Exception($"Browser installation failed with exit code {exitCode}");
            }
        }
        catch (Exception ex)
        {
            Status = BrowserStatus.Error;
            _logger.LogError(ex, "Error during browser installation");
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
            await InitializeAsync();
        }

        if (!IsBrowserInstalled)
        {
            throw new InvalidOperationException("Browser not installed. Call InstallBrowsersAsync first.");
        }

        // Thread-safe browser creation
        await _initLock.WaitAsync();
        try
        {
            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            // Launch new browser
            _logger.LogInformation("Launching Chromium browser...");
            _browser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-blink-features=AutomationControlled"]
            });

            _logger.LogInformation("Browser launched successfully");
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IPage> CreatePageAsync(string userAgent, ProxySettings? proxySettings = null)
    {
        var browser = await GetBrowserAsync();
        
        // IMPORTANT: We create a new context for each page
        // The context must be disposed when the page is closed
        // This is handled by disposing the page's context in the page cleanup
        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            UserAgent = userAgent
        };

        // Configure proxy if provided and enabled
        if (proxySettings is { Enabled: true } && !string.IsNullOrWhiteSpace(proxySettings.Server))
        {
            _logger.LogInformation("Creating page with proxy: {ProxyUrl}", proxySettings.GetProxyUrl());
            
            contextOptions.Proxy = new Proxy
            {
                Server = proxySettings.GetProxyUrl()
            };

            // Add authentication if required
            if (proxySettings.RequiresAuthentication && !string.IsNullOrWhiteSpace(proxySettings.Username))
            {
                contextOptions.Proxy.Username = proxySettings.Username;
                contextOptions.Proxy.Password = proxySettings.GetPassword();
            }

            // Add bypass list
            if (proxySettings.BypassList.Count > 0)
            {
                contextOptions.Proxy.Bypass = string.Join(",", proxySettings.BypassList);
            }
        }

        var context = await browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();
        
        // Set default timeout
        page.SetDefaultTimeout(30000); // 30 seconds
        page.SetDefaultNavigationTimeout(30000);

        return page;
    }
    
    /// <summary>
    /// Properly dispose a page and its context to prevent memory leaks.
    /// </summary>
    public async Task ClosePageAsync(IPage page)
    {
        try
        {
            var context = page.Context;
            await page.CloseAsync();
            await context.CloseAsync();
            await context.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing page");
        }
    }

    public async Task DisposeAsync()
    {
        // Use lock-free check-and-set pattern to prevent double disposal
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 1)
            return; // Already disposed

        try
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                await _browser.DisposeAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
            
            // Dispose semaphore last
            _initLock?.Dispose();
            
            _logger.LogInformation("Playwright resources disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Playwright resources");
        }
    }

    public void Dispose()
    {
        // Use lock-free check-and-set pattern to prevent double disposal
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 1)
            return; // Already disposed
        
        _logger.LogWarning("Synchronous Dispose() called - prefer DisposeAsync() for proper cleanup");
        
        // Perform synchronous disposal with timeout
        try
        {
            if (_browser != null || _playwright != null)
            {
                var disposeTask = Task.Run(async () =>
                {
                    try
                    {
                        if (_browser != null)
                        {
                            await _browser.CloseAsync();
                            await _browser.DisposeAsync();
                            _browser = null;
                        }

                        _playwright?.Dispose();
                        _playwright = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing Playwright resources in synchronous Dispose");
                    }
                });
                
                if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogWarning("Playwright synchronous disposal timed out after 5 seconds");
                }
            }
            
            // Dispose semaphore
            _initLock?.Dispose();
            
            _logger.LogInformation("Playwright resources disposed (synchronous path)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in synchronous Dispose");
        }
    }

    private async Task<bool> CheckBrowserInstalledAsync()
    {
        try
        {
            // Try to launch browser briefly to check if it's installed
            var testBrowser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = 5000
            });
            
            await testBrowser.CloseAsync();
            await testBrowser.DisposeAsync();
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> RunPlaywrightInstallAsync(IProgress<string>? progress)
    {
        // For .NET Playwright, use the built-in Program.Main method
        // This is the official way to install browsers for Microsoft.Playwright NuGet package
        
        return await Task.Run(() =>
        {
            try
            {
                progress?.Report("Installing Chromium browser...");
                
                // Use the built-in Playwright installation
                // This calls the playwright.ps1 script bundled with the NuGet package
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
                _logger.LogError(ex, "Error during Playwright installation");
                progress?.Report($"Error: {ex.Message}");
                return -1;
            }
        });
    }
}

