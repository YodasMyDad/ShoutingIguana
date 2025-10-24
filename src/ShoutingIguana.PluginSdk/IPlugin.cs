namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Represents a plugin that can be loaded by the Shouting Iguana application.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Unique identifier for this plugin (e.g., "com.shoutingiguana.brokenlinks").
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Display name for this plugin (e.g., "Broken Links Analyzer").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Plugin version.
    /// </summary>
    Version Version { get; }
    
    /// <summary>
    /// Optional description of what this plugin does.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Called when the plugin is loaded. Register tasks and exporters here.
    /// </summary>
    void Initialize(IHostContext context);
}

