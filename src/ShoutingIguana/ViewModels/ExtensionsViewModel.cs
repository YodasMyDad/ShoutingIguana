using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Core.Services.NuGet;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class ExtensionsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<ExtensionsViewModel> _logger;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly INuGetService _nuGetService;
    private readonly IPackageManagerService _packageManager;
    private readonly IToastService _toastService;
    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<InstalledPluginViewModel> _installedPlugins = [];

    [ObservableProperty]
    private ObservableCollection<BrowsePluginViewModel> _browseResults = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallPluginCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePluginCommand))]
    private InstalledPluginViewModel? _selectedInstalledPlugin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPluginCommand))]
    private BrowsePluginViewModel? _selectedBrowsePlugin;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalPlugins;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _installStatus = string.Empty;

    [ObservableProperty]
    private int _installProgress;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private int _updatesAvailable;

    [ObservableProperty]
    private bool _includePrerelease;

    public ExtensionsViewModel(
        ILogger<ExtensionsViewModel> logger,
        IPluginRegistry pluginRegistry,
        INuGetService nuGetService,
        IPackageManagerService packageManager,
        IToastService toastService)
    {
        _logger = logger;
        _pluginRegistry = pluginRegistry;
        _nuGetService = nuGetService;
        _packageManager = packageManager;
        _toastService = toastService;

        // Subscribe to plugin events
        _pluginRegistry.PluginLoaded += OnPluginLoaded;
        _pluginRegistry.PluginUnloaded += OnPluginUnloaded;

        // Load data
        _ = LoadInstalledPluginsAsync();
        _ = CheckForUpdatesAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        TriggerDebouncedSearch();
    }

    partial void OnIncludePrereleaseChanged(bool value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        TriggerDebouncedSearch();
    }

    private void TriggerDebouncedSearch()
    {
        // Debounced search - dispose old token before creating new one
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token); // Debounce 300ms
                if (!token.IsCancellationRequested)
                {
                    await SearchPluginsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when typing quickly
            }
        }, token);
    }

    private async Task LoadInstalledPluginsAsync()
    {
        IsLoading = true;
        try
        {
            var loadedPlugins = _pluginRegistry.LoadedPlugins;
            var registeredTasks = _pluginRegistry.RegisteredTasks;
            var installedPackages = await _packageManager.GetInstalledPluginsAsync();

            var viewModels = new List<InstalledPluginViewModel>();

            foreach (var plugin in loadedPlugins)
            {
                var packageInfo = installedPackages.FirstOrDefault(p =>
                    p.PluginId.Equals(plugin.Id, StringComparison.OrdinalIgnoreCase));

                viewModels.Add(new InstalledPluginViewModel
                {
                    PluginId = plugin.Id,
                    Name = plugin.Name,
                    Version = plugin.Version.ToString(),
                    Description = plugin.Description,
                    Status = "Loaded",
                    TaskCount = registeredTasks.Count(t => GetPluginForTask(t) == plugin),
                    PackageId = packageInfo?.PackageId ?? "built-in",
                    IsBuiltIn = packageInfo == null
                });
            }

            InstalledPlugins = new ObservableCollection<InstalledPluginViewModel>(viewModels);
            TotalPlugins = InstalledPlugins.Count;
            TotalTasks = registeredTasks.Count;

            _logger.LogInformation("Loaded {Count} installed plugins", TotalPlugins);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load installed plugins");
            _toastService.ShowError("Error", "Failed to load installed plugins");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchPluginsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await Application.Current.Dispatcher.InvokeAsync(() => BrowseResults.Clear());
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() => IsSearching = true);
        try
        {
            var results = await _nuGetService.SearchPackagesAsync(
                SearchQuery,
                tagFilter: null,
                includePrerelease: IncludePrerelease,
                skip: 0,
                take: 50);

            var viewModels = results.Select(r => new BrowsePluginViewModel
            {
                PackageId = r.Id,
                Name = r.Id,
                Version = r.Version,
                Description = r.Description ?? "No description available",
                Authors = r.Authors ?? "Unknown",
                DownloadCount = r.DownloadCount,
                IsInstalled = InstalledPlugins.Any(p => p.PackageId.Equals(r.Id, StringComparison.OrdinalIgnoreCase))
            }).ToList();

            await Application.Current.Dispatcher.InvokeAsync(() => 
            {
                BrowseResults = new ObservableCollection<BrowsePluginViewModel>(viewModels);
            });
            
            _logger.LogInformation("Found {Count} packages matching '{Query}'", viewModels.Count, SearchQuery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search plugins");
            _toastService.ShowError("Search Failed", $"Failed to search for plugins: {ex.Message}");
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsSearching = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallPlugin))]
    private async Task InstallPluginAsync()
    {
        if (SelectedBrowsePlugin == null) return;

        IsInstalling = true;
        InstallStatus = "Validating package...";
        InstallProgress = 0;

        string? tempPackagePath = null;

        try
        {
            // Step 1: Download package to temp location for validation
            // Use a unique directory per installation to avoid conflicts
            var tempDir = Path.Combine(Path.GetTempPath(), "ShoutingIguana", "ValidatePackage", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            var downloadProgress = new Progress<int>(p =>
            {
                InstallStatus = $"Downloading package... {p}%";
                InstallProgress = p / 2; // First half of progress bar
            });

            tempPackagePath = await _nuGetService.DownloadPackageAsync(
                SelectedBrowsePlugin.PackageId,
                SelectedBrowsePlugin.Version,
                tempDir,
                downloadProgress);

            // Step 2: Validate that package contains a valid plugin
            InstallStatus = "Validating plugin...";
            InstallProgress = 50;

            var validation = await _nuGetService.ValidatePackageAsync(tempPackagePath);

            if (validation.Result != PackageValidationResult.Valid)
            {
                var errorMessage = validation.Result switch
                {
                    PackageValidationResult.NoPlugin => 
                        "This package does not contain a valid Shouting Iguana plugin. " +
                        "Plugins must implement IPlugin interface and have the [Plugin] attribute.",
                    PackageValidationResult.IncompatibleSdk => 
                        $"This plugin requires SDK version {validation.MinSdkVersion} but the current version is incompatible. " +
                        "Please update Shouting Iguana or contact the plugin developer.",
                    PackageValidationResult.InvalidPackage => 
                        $"Invalid or corrupted package: {validation.ErrorMessage}",
                    _ => $"Package validation failed: {validation.ErrorMessage}"
                };

                _toastService.ShowError("Invalid Plugin Package", errorMessage);
                _logger.LogWarning("Package validation failed for {PackageId}: {Result} - {Error}", 
                    SelectedBrowsePlugin.PackageId, validation.Result, validation.ErrorMessage);
                return;
            }

            // Step 3: Proceed with installation
            InstallStatus = "Installing plugin...";
            InstallProgress = 60;

            var installProgress = new Progress<InstallProgress>(p =>
            {
                InstallStatus = p.Status;
                InstallProgress = 60 + (p.PercentComplete * 40 / 100); // Last 40% of progress bar
            });

            var result = await _packageManager.InstallPluginAsync(
                SelectedBrowsePlugin.PackageId,
                SelectedBrowsePlugin.Version,
                installProgress);

            if (result.Success)
            {
                _toastService.ShowSuccess("Plugin Installed", 
                    $"{validation.PluginName ?? result.PluginName} has been installed successfully");
                SelectedBrowsePlugin.IsInstalled = true;
                
                // Refresh installed list
                await LoadInstalledPluginsAsync();
            }
            else
            {
                _toastService.ShowError("Installation Failed", result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin: {PackageId}", SelectedBrowsePlugin.PackageId);
            _toastService.ShowError("Installation Failed", $"Failed to install plugin: {ex.Message}");
        }
        finally
        {
            // Clean up temp package file
            if (tempPackagePath != null)
            {
                try
                {
                    var tempDir = Path.GetDirectoryName(tempPackagePath);
                    if (tempDir != null && Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temp package directory");
                }
            }

            IsInstalling = false;
            InstallStatus = string.Empty;
            InstallProgress = 0;
        }
    }

    private bool CanInstallPlugin() => SelectedBrowsePlugin != null && !SelectedBrowsePlugin.IsInstalled;

    [RelayCommand(CanExecute = nameof(CanUninstallPlugin))]
    private async Task UninstallPluginAsync()
    {
        if (SelectedInstalledPlugin == null || SelectedInstalledPlugin.IsBuiltIn) return;

        var result = MessageBox.Show(
            $"Are you sure you want to uninstall {SelectedInstalledPlugin.Name}?{Environment.NewLine}{Environment.NewLine}This action cannot be undone.",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _packageManager.UninstallPluginAsync(SelectedInstalledPlugin.PluginId);
            _toastService.ShowSuccess("Plugin Uninstalled", $"{SelectedInstalledPlugin.Name} has been uninstalled");

            // Refresh lists
            await LoadInstalledPluginsAsync();
            
            // Update browse results if any
            var browseItem = BrowseResults.FirstOrDefault(b => 
                b.PackageId.Equals(SelectedInstalledPlugin.PackageId, StringComparison.OrdinalIgnoreCase));
            if (browseItem != null)
            {
                browseItem.IsInstalled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall plugin: {PluginId}", SelectedInstalledPlugin.PluginId);
            _toastService.ShowError("Uninstall Failed", $"Failed to uninstall plugin: {ex.Message}");
        }
    }

    private bool CanUninstallPlugin() => SelectedInstalledPlugin != null && !SelectedInstalledPlugin.IsBuiltIn;

    [RelayCommand(CanExecute = nameof(CanUpdatePlugin))]
    private async Task UpdatePluginAsync()
    {
        if (SelectedInstalledPlugin == null || SelectedInstalledPlugin.IsBuiltIn) return;

        IsInstalling = true;
        InstallStatus = "Updating...";
        InstallProgress = 0;

        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallStatus = p.Status;
                InstallProgress = p.PercentComplete;
            });

            var result = await _packageManager.UpdatePluginAsync(SelectedInstalledPlugin.PluginId, progress);

            if (result.Success)
            {
                _toastService.ShowSuccess("Plugin Updated", $"{result.PluginName} has been updated successfully");
                await LoadInstalledPluginsAsync();
                await CheckForUpdatesAsync();
            }
            else
            {
                _toastService.ShowError("Update Failed", result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update plugin: {PluginId}", SelectedInstalledPlugin.PluginId);
            _toastService.ShowError("Update Failed", $"Failed to update plugin: {ex.Message}");
        }
        finally
        {
            IsInstalling = false;
            InstallStatus = string.Empty;
            InstallProgress = 0;
        }
    }

    private bool CanUpdatePlugin() => SelectedInstalledPlugin != null && !SelectedInstalledPlugin.IsBuiltIn && SelectedInstalledPlugin.HasUpdate;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updates = await _packageManager.CheckForUpdatesAsync();
            UpdatesAvailable = updates.Count;

            // Mark plugins that have updates
            foreach (var update in updates)
            {
                var plugin = InstalledPlugins.FirstOrDefault(p => 
                    p.PluginId.Equals(update.PluginId, StringComparison.OrdinalIgnoreCase));
                if (plugin != null)
                {
                    plugin.HasUpdate = true;
                }
            }

            if (updates.Count > 0)
            {
                _logger.LogInformation("{Count} plugin updates available", updates.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
    }

    [RelayCommand]
    private async Task RefreshPluginsAsync()
    {
        await LoadInstalledPluginsAsync();
        await CheckForUpdatesAsync();
        _toastService.ShowInfo("Refreshed", "Plugin list has been refreshed");
    }

    private PluginSdk.IPlugin? GetPluginForTask(PluginSdk.IUrlTask task)
    {
        return _pluginRegistry.LoadedPlugins.FirstOrDefault(p =>
            task.Key.Contains(p.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }

    private void OnPluginLoaded(object? sender, PluginEventArgs e)
    {
        // Refresh UI on plugin load - use InvokeAsync for proper async handling
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await LoadInstalledPluginsAsync();
        });
    }

    private void OnPluginUnloaded(object? sender, PluginEventArgs e)
    {
        // Refresh UI on plugin unload - use InvokeAsync for proper async handling
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await LoadInstalledPluginsAsync();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Unsubscribe from events to prevent memory leak
        _pluginRegistry.PluginLoaded -= OnPluginLoaded;
        _pluginRegistry.PluginUnloaded -= OnPluginUnloaded;

        // Cancel and dispose search token
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        _disposed = true;
    }
}

public partial class InstalledPluginViewModel : ObservableObject
{
    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private string _packageId = string.Empty;

    [ObservableProperty]
    private bool _isBuiltIn;

    [ObservableProperty]
    private bool _hasUpdate;
}

public partial class BrowsePluginViewModel : ObservableObject
{
    [ObservableProperty]
    private string _packageId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _authors = string.Empty;

    [ObservableProperty]
    private long _downloadCount;

    [ObservableProperty]
    private bool _isInstalled;
}
