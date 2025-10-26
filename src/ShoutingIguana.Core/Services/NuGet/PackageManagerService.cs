using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ShoutingIguana.Core.Services.NuGet;

/// <summary>
/// Implementation of IPackageManagerService.
/// </summary>
public class PackageManagerService(
    ILogger<PackageManagerService> logger,
    INuGetService nuGetService,
    IPluginRegistry pluginRegistry) : IPackageManagerService
{
    private readonly ILogger<PackageManagerService> _logger = logger;
    private readonly INuGetService _nuGetService = nuGetService;
    private readonly IPluginRegistry _pluginRegistry = pluginRegistry;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static string GetExtensionsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var extensionsDir = Path.Combine(appData, "ShoutingIguana", "extensions");
        Directory.CreateDirectory(extensionsDir);
        return extensionsDir;
    }

    private static string GetMetadataPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(appData, "ShoutingIguana");
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, "installed-plugins.json");
    }

    public async Task<InstallResult> InstallPluginAsync(
        string packageId,
        string version,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            progress?.Report(new InstallProgress { Status = "Downloading...", PercentComplete = 0 });

            // Download package
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var downloadProgress = new Progress<int>(p =>
                {
                    progress?.Report(new InstallProgress { Status = "Downloading...", PercentComplete = p / 4 });
                });

                var packagePath = await _nuGetService.DownloadPackageAsync(
                    packageId,
                    version,
                    tempDir,
                    downloadProgress,
                    cancellationToken);

                progress?.Report(new InstallProgress { Status = "Validating...", PercentComplete = 30 });

                // Validate package
                var validation = await _nuGetService.ValidatePackageAsync(packagePath, cancellationToken);

                if (validation.Result != PackageValidationResult.Valid)
                {
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = validation.ErrorMessage ?? "Package validation failed"
                    };
                }

                progress?.Report(new InstallProgress { Status = "Extracting...", PercentComplete = 50 });

                // Extract package to extensions directory
                var extensionsPath = GetExtensionsPath();
                var pluginDir = Path.Combine(extensionsPath, packageId, version);

                if (Directory.Exists(pluginDir))
                {
                    Directory.Delete(pluginDir, true);
                }

                Directory.CreateDirectory(pluginDir);

                ZipFile.ExtractToDirectory(packagePath, pluginDir);

                progress?.Report(new InstallProgress { Status = "Loading plugin...", PercentComplete = 75 });

                // Find the lib folder with .NET assemblies
                var libDir = Directory.GetDirectories(pluginDir, "lib", SearchOption.AllDirectories).FirstOrDefault();
                if (libDir != null)
                {
                    // Find the target framework folder (prefer net9.0, net8.0, net6.0, netstandard2.1, netstandard2.0)
                    var targetFrameworks = new[] { "net9.0", "net8.0", "net6.0", "netstandard2.1", "netstandard2.0" };
                    string? targetDir = null;

                    foreach (var framework in targetFrameworks)
                    {
                        var fxDir = Path.Combine(libDir, framework);
                        if (Directory.Exists(fxDir))
                        {
                            targetDir = fxDir;
                            break;
                        }
                    }

                    if (targetDir != null)
                    {
                        // Load plugin from the target framework directory
                        await _pluginRegistry.LoadPluginAsync(targetDir);
                    }
                    else
                    {
                        // Try loading from lib directory directly
                        await _pluginRegistry.LoadPluginAsync(libDir);
                    }
                }
                else
                {
                    return new InstallResult
                    {
                        Success = false,
                        ErrorMessage = "Package does not contain a lib folder"
                    };
                }

                // Save metadata
                await SaveInstalledPluginMetadataAsync(new InstalledPluginInfo
                {
                    PluginId = validation.PluginId!,
                    PluginName = validation.PluginName!,
                    Version = version,
                    PackageId = packageId,
                    InstallPath = pluginDir
                });

                progress?.Report(new InstallProgress { Status = "Complete", PercentComplete = 100 });

                _logger.LogInformation("Successfully installed plugin: {PackageId} v{Version}", packageId, version);

                return new InstallResult
                {
                    Success = true,
                    PluginId = validation.PluginId,
                    PluginName = validation.PluginName
                };
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin: {PackageId}", packageId);
            return new InstallResult
            {
                Success = false,
                ErrorMessage = $"Installation failed: {ex.Message}"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UninstallPluginAsync(string pluginId)
    {
        await _lock.WaitAsync();
        try
        {
            // Get metadata
            var metadata = await GetInstalledPluginsAsync();
            var pluginInfo = metadata.FirstOrDefault(p => p.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase));

            if (pluginInfo == null)
            {
                _logger.LogWarning("Plugin not found for uninstall: {PluginId}", pluginId);
                return;
            }

            // Unload plugin
            await _pluginRegistry.UnloadPluginAsync(pluginId);

            // Delete installation directory
            if (Directory.Exists(pluginInfo.InstallPath))
            {
                try
                {
                    // Try to delete, may fail if files are locked
                    Directory.Delete(pluginInfo.InstallPath, true);

                    // Also try to delete parent package directory if empty
                    var packageDir = Path.GetDirectoryName(pluginInfo.InstallPath);
                    if (packageDir != null && Directory.Exists(packageDir))
                    {
                        if (!Directory.EnumerateFileSystemEntries(packageDir).Any())
                        {
                            Directory.Delete(packageDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete plugin directory: {Path}", pluginInfo.InstallPath);
                }
            }

            // Remove from metadata
            await RemoveInstalledPluginMetadataAsync(pluginId);

            _logger.LogInformation("Uninstalled plugin: {PluginId}", pluginId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<InstallResult> UpdatePluginAsync(
        string pluginId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetInstalledPluginsAsync();
        var pluginInfo = metadata.FirstOrDefault(p => p.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase));

        if (pluginInfo == null)
        {
            return new InstallResult
            {
                Success = false,
                ErrorMessage = $"Plugin {pluginId} not found"
            };
        }

        // Get latest version
        var packageMetadata = await _nuGetService.GetPackageMetadataAsync(pluginInfo.PackageId, null, cancellationToken);
        if (packageMetadata == null)
        {
            return new InstallResult
            {
                Success = false,
                ErrorMessage = $"Failed to get metadata for {pluginInfo.PackageId}"
            };
        }

        // Uninstall old version
        progress?.Report(new InstallProgress { Status = "Uninstalling old version...", PercentComplete = 10 });
        await UninstallPluginAsync(pluginId);

        // Install new version
        return await InstallPluginAsync(pluginInfo.PackageId, packageMetadata.Version, progress, cancellationToken);
    }

    public async Task<IReadOnlyList<InstalledPluginInfo>> GetInstalledPluginsAsync()
    {
        var metadataPath = GetMetadataPath();

        if (!File.Exists(metadataPath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var plugins = JsonSerializer.Deserialize<List<InstalledPluginInfo>>(json) ?? [];
            return plugins.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load installed plugins metadata");
            return [];
        }
    }

    public async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var updates = new List<PluginUpdateInfo>();
        var installed = await GetInstalledPluginsAsync();

        foreach (var plugin in installed)
        {
            try
            {
                var metadata = await _nuGetService.GetPackageMetadataAsync(plugin.PackageId, null, cancellationToken);
                if (metadata != null && metadata.Version != plugin.Version)
                {
                    // Compare versions
                    if (Version.TryParse(metadata.Version, out var latestVersion) &&
                        Version.TryParse(plugin.Version, out var currentVersion) &&
                        latestVersion > currentVersion)
                    {
                        updates.Add(new PluginUpdateInfo
                        {
                            PluginId = plugin.PluginId,
                            PluginName = plugin.PluginName,
                            CurrentVersion = plugin.Version,
                            LatestVersion = metadata.Version,
                            PackageId = plugin.PackageId
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for updates for {PackageId}", plugin.PackageId);
            }
        }

        return updates;
    }

    private async Task SaveInstalledPluginMetadataAsync(InstalledPluginInfo plugin)
    {
        var metadata = (await GetInstalledPluginsAsync()).ToList();
        
        // Remove existing entry if present
        metadata.RemoveAll(p => p.PluginId.Equals(plugin.PluginId, StringComparison.OrdinalIgnoreCase));
        
        metadata.Add(plugin);

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetMetadataPath(), json);
    }

    private async Task RemoveInstalledPluginMetadataAsync(string pluginId)
    {
        var metadata = (await GetInstalledPluginsAsync()).ToList();
        metadata.RemoveAll(p => p.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase));

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetMetadataPath(), json);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}

