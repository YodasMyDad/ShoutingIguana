using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Registry for managing plugins and their tasks.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// All loaded plugins.
    /// </summary>
    IReadOnlyList<IPlugin> LoadedPlugins { get; }
    
    /// <summary>
    /// All registered URL tasks from plugins.
    /// </summary>
    IReadOnlyList<IUrlTask> RegisteredTasks { get; }
    
    /// <summary>
    /// All registered export providers from plugins.
    /// </summary>
    IReadOnlyList<IExportProvider> RegisteredExporters { get; }
    
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
}

