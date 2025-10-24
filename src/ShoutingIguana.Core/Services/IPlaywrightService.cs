using Microsoft.Playwright;

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
    /// Create a new browser page with configured settings.
    /// </summary>
    Task<IPage> CreatePageAsync();
    
    /// <summary>
    /// Properly close a page and dispose its context to prevent memory leaks.
    /// </summary>
    Task ClosePageAsync(IPage page);
    
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

