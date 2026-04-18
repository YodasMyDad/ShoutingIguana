namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Trust policy for plugin loading. When <see cref="Enabled"/> is true,
/// the plugin registry will only load assemblies whose simple name appears
/// in <see cref="Allowlist"/>. Assembly-name matching is case-insensitive.
/// </summary>
/// <remarks>
/// This is a defense-in-depth measure — a plugin still runs with full process
/// trust once loaded. Authenticode signing and hash pinning are future work.
/// </remarks>
public class PluginTrustSettings
{
    /// <summary>
    /// Whether allowlist enforcement is active. When false, every DLL in the
    /// plugins directory is loaded (legacy behaviour). When true, only
    /// assemblies in <see cref="Allowlist"/> load.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Assembly simple names permitted to load as plugins. Names are matched
    /// case-insensitively against <c>Path.GetFileNameWithoutExtension(dllPath)</c>.
    /// </summary>
    public List<string> Allowlist { get; set; } = new()
    {
        "ShoutingIguana.Plugins"
    };
}
