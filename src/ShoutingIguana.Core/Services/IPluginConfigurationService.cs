namespace ShoutingIguana.Core.Services;

/// <summary>
/// Service for managing plugin enabled/disabled state.
/// </summary>
public interface IPluginConfigurationService
{
    /// <summary>
    /// Checks if a plugin is enabled.
    /// </summary>
    /// <param name="pluginId">The unique plugin identifier</param>
    /// <returns>True if the plugin is enabled, false otherwise</returns>
    Task<bool> IsPluginEnabledAsync(string pluginId);
    
    /// <summary>
    /// Sets the enabled state of a plugin.
    /// </summary>
    /// <param name="pluginId">The unique plugin identifier</param>
    /// <param name="enabled">True to enable the plugin, false to disable it</param>
    Task SetPluginEnabledAsync(string pluginId, bool enabled);
    
    /// <summary>
    /// Gets all plugin states (enabled/disabled).
    /// </summary>
    /// <returns>Dictionary mapping plugin IDs to their enabled state</returns>
    Task<Dictionary<string, bool>> GetAllPluginStatesAsync();
    
    /// <summary>
    /// Event raised when a plugin's enabled state changes.
    /// </summary>
    event EventHandler<PluginStateChangedEventArgs>? PluginStateChanged;
}

/// <summary>
/// Event arguments for plugin state changes.
/// </summary>
public class PluginStateChangedEventArgs : EventArgs
{
    public string PluginId { get; }
    public bool IsEnabled { get; }
    
    public PluginStateChangedEventArgs(string pluginId, bool isEnabled)
    {
        PluginId = pluginId;
        IsEnabled = isEnabled;
    }
}

