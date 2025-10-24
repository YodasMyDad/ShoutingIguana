namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Attribute to mark a class as a plugin.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PluginAttribute : Attribute
{
    /// <summary>
    /// Unique plugin identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Plugin display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Minimum SDK version required by this plugin.
    /// </summary>
    public string MinSdkVersion { get; set; } = "1.0.0";
}

