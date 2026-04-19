using Microsoft.Playwright;
using ShoutingIguana.Core.Configuration;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for managing Playwright browser instances.
/// </summary>
public interface IPlaywrightService
{
    /// <summary>
    /// Indicates if Playwright browsers are installed.
    /// </summary>
    bool IsBrowserInstalled { get; }
    
    /// <summary>
    /// Current status of the browser.
    /// </summary>
    BrowserStatus Status { get; }
    
    /// <summary>
    /// Event raised when browser status changes.
    /// </summary>
    event EventHandler<BrowserStatusEventArgs>? StatusChanged;
    
    /// <summary>
    /// Initialize Playwright and check browser installation.
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// Install Playwright browsers if not already installed.
    /// </summary>
    /// <param name="progress">Progress callback for installation updates</param>
    Task InstallBrowsersAsync(IProgress<string>? progress = null);
    
    /// <summary>
    /// Get or create a browser instance.
    /// </summary>
    Task<IBrowser> GetBrowserAsync();
    
    /// <summary>
    /// Borrow a page from the pool. The underlying <see cref="IBrowserContext"/> is reused
    /// across borrows; UA is applied per-page via extra HTTP headers.
    /// </summary>
    /// <param name="userAgent">User agent string to use for this page</param>
    /// <param name="proxySettings">Optional proxy settings to use for this page</param>
    /// <param name="blockNonEssentialResources">
    /// When true, aborts requests for images, media, and fonts so pages load only the
    /// HTML, CSS, and JS needed for link and metadata extraction.
    /// </param>
    Task<IPage> CreatePageAsync(string userAgent, ProxySettings? proxySettings = null, bool blockNonEssentialResources = true);

    /// <summary>
    /// Returns the page's context to the pool, or disposes it if the context has faulted.
    /// </summary>
    Task ClosePageAsync(IPage page);

    /// <summary>
    /// Configure the maximum number of concurrently borrowed contexts. Should be called
    /// before a crawl starts with the active project's ConcurrentRequests.
    /// </summary>
    void ConfigurePoolSize(int size);
    
    /// <summary>
    /// Dispose resources.
    /// </summary>
    Task DisposeAsync();
}

public enum BrowserStatus
{
    NotInitialized,
    Initializing,
    Installing,
    Ready,
    Error
}

public class BrowserStatusEventArgs(BrowserStatus status, string? message = null) : EventArgs
{
    public BrowserStatus Status { get; } = status;
    public string? Message { get; } = message;
}

