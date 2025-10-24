using Microsoft.Extensions.Logging;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Context provided to plugins during initialization.
/// Allows plugins to register their tasks and exporters.
/// </summary>
public interface IHostContext
{
    /// <summary>
    /// Register a URL task that will be executed for each crawled URL.
    /// </summary>
    void RegisterTask(IUrlTask task);
    
    /// <summary>
    /// Register an export provider that can export findings.
    /// </summary>
    void RegisterExport(IExportProvider export);
    
    /// <summary>
    /// Create a logger for the plugin.
    /// </summary>
    ILogger CreateLogger(string categoryName);
    
    /// <summary>
    /// Create a typed logger for the plugin.
    /// </summary>
    ILogger<T> CreateLogger<T>();
    
    /// <summary>
    /// Get the service provider for dependency injection.
    /// </summary>
    IServiceProvider GetServiceProvider();
}

