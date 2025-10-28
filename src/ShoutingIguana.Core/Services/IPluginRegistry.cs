using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Registry for managing plugins and their tasks.
/// </summary>
public interface IPluginRegistry : IDisposable
{
    /// <summary>
    /// All loaded plugins (includes both enabled and disabled plugins).
    /// </summary>
    IReadOnlyList<IPlugin> LoadedPlugins { get; }
    
    /// <summary>
    /// Only enabled plugins (filtered by plugin configuration).
    /// </summary>
    IReadOnlyList<IPlugin> EnabledPlugins { get; }
    
    /// <summary>
    /// All registered URL tasks from plugins (includes tasks from disabled plugins).
    /// </summary>
    IReadOnlyList<IUrlTask> RegisteredTasks { get; }
    
    /// <summary>
    /// Only tasks from enabled plugins.
    /// </summary>
    IReadOnlyList<IUrlTask> EnabledTasks { get; }
    
    /// <summary>
    /// All registered export providers from plugins (includes providers from disabled plugins).
    /// </summary>
    IReadOnlyList<IExportProvider> RegisteredExporters { get; }
    
    /// <summary>
    /// Only export providers from enabled plugins.
    /// </summary>
    IReadOnlyList<IExportProvider> EnabledExporters { get; }
    
    /// <summary>
    /// Discover and load plugins from the plugins directory.
    /// </summary>
    Task LoadPluginsAsync();
    
    /// <summary>
    /// Get tasks by priority order.
    /// </summary>
    IReadOnlyList<IUrlTask> GetTasksByPriority();
    
    /// <summary>
    /// Get plugin by ID.
    /// </summary>
    IPlugin? GetPluginById(string id);
    
    /// <summary>
    /// Event raised when a plugin is loaded.
    /// </summary>
    event EventHandler<PluginEventArgs>? PluginLoaded;
    
    /// <summary>
    /// Event raised when a plugin is unloaded.
    /// </summary>
    event EventHandler<PluginEventArgs>? PluginUnloaded;
    
    /// <summary>
    /// Hot-load a plugin from the specified package path.
    /// </summary>
    Task LoadPluginAsync(string packagePath);
    
    /// <summary>
    /// Unload a plugin by ID.
    /// </summary>
    Task UnloadPluginAsync(string pluginId);
    
    /// <summary>
    /// Reload a plugin (unload then load).
    /// </summary>
    Task ReloadPluginAsync(string pluginId);
    
    /// <summary>
    /// Check if a plugin is currently active.
    /// </summary>
    Task<bool> IsPluginActiveAsync(string pluginId);
}

/// <summary>
/// Event args for plugin load/unload events.
/// </summary>
public class PluginEventArgs(IPlugin plugin) : EventArgs
{
    public IPlugin Plugin { get; } = plugin;
}

