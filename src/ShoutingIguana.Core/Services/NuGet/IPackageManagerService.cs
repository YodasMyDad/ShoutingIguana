namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Service for managing plugin packages.
/// </summary>
public interface IPackageManagerService : IDisposable
{
    /// <summary>
    /// Install a plugin package.
    /// </summary>
    Task<InstallResult> InstallPluginAsync(
        string packageId,
        string version,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstall a plugin.
    /// </summary>
    Task UninstallPluginAsync(string pluginId);

    /// <summary>
    /// Update a plugin to the latest version.
    /// </summary>
    Task<InstallResult> UpdatePluginAsync(
        string pluginId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all installed plugins.
    /// </summary>
    Task<IReadOnlyList<InstalledPluginInfo>> GetInstalledPluginsAsync();

    /// <summary>
    /// Check for available updates for installed plugins.
    /// </summary>
    Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a plugin installation.
/// </summary>
public class InstallResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PluginId { get; init; }
    public string? PluginName { get; init; }
}

/// <summary>
/// Progress information for installation.
/// </summary>
public class InstallProgress
{
    public required string Status { get; init; }
    public int PercentComplete { get; init; }
}

/// <summary>
/// Information about an installed plugin.
/// </summary>
public class InstalledPluginInfo
{
    public required string PluginId { get; init; }
    public required string PluginName { get; init; }
    public required string Version { get; init; }
    public required string PackageId { get; init; }
    public required string InstallPath { get; init; }
}

/// <summary>
/// Information about a plugin update.
/// </summary>
public class PluginUpdateInfo
{
    public required string PluginId { get; init; }
    public required string PluginName { get; init; }
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string PackageId { get; init; }
}

