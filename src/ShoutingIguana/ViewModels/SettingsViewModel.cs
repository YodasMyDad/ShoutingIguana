using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Configuration;
using ShoutingIguana.Core.Services;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IAppSettingsService _appSettings;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly Window _dialog;

    // Crawling settings
    [ObservableProperty] private int _concurrentRequests = 2;
    [ObservableProperty] private int _timeoutSeconds = 30;
    [ObservableProperty] private int _maxCrawlDepth = 5;
    [ObservableProperty] private int _maxUrlsToCrawl = 10000;
    [ObservableProperty] private bool _respectRobotsTxt = true;
    [ObservableProperty] private bool _useSitemapXml = true;
    [ObservableProperty] private double _crawlDelaySeconds = 1.0;

    // Browser settings
    [ObservableProperty] private bool _headless = true;
    [ObservableProperty] private int _viewportWidth = 1920;
    [ObservableProperty] private int _viewportHeight = 1080;
    [ObservableProperty] private int _memoryLimitMb = 1536;

    // Plugin settings
    [ObservableProperty] private ObservableCollection<PluginInfo> _plugins = [];
    [ObservableProperty] private bool _pluginsChanged;

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        IAppSettingsService appSettings,
        IPluginRegistry pluginRegistry,
        Window dialog)
    {
        _logger = logger;
        _appSettings = appSettings;
        _pluginRegistry = pluginRegistry;
        _dialog = dialog;

        LoadSettings();
        LoadPlugins();
    }

    private void LoadSettings()
    {
        // Load browser settings
        Headless = _appSettings.BrowserSettings.Headless;
        ViewportWidth = _appSettings.BrowserSettings.ViewportWidth;
        ViewportHeight = _appSettings.BrowserSettings.ViewportHeight;

        // Load crawl settings from app settings (if available)
        var crawlSettings = _appSettings.CrawlSettings;
        if (crawlSettings != null)
        {
            ConcurrentRequests = crawlSettings.ConcurrentRequests;
            TimeoutSeconds = crawlSettings.TimeoutSeconds;
            MaxCrawlDepth = crawlSettings.MaxCrawlDepth;
            MaxUrlsToCrawl = crawlSettings.MaxUrlsToCrawl;
            RespectRobotsTxt = crawlSettings.RespectRobotsTxt;
            UseSitemapXml = crawlSettings.UseSitemapXml;
            CrawlDelaySeconds = crawlSettings.CrawlDelaySeconds;
            MemoryLimitMb = crawlSettings.MemoryLimitMb;
        }

        _logger.LogDebug("Settings loaded");
    }

    private void LoadPlugins()
    {
        var loadedPlugins = _pluginRegistry.LoadedPlugins;
        
        foreach (var plugin in loadedPlugins)
        {
            Plugins.Add(new PluginInfo
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Version = plugin.Version.ToString(),
                IsEnabled = true // In Stage 2, all plugins are always enabled
            });
        }

        _logger.LogDebug("Loaded {Count} plugins", Plugins.Count);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _logger.LogInformation("Saving settings...");

            // Validate settings
            if (!ValidateSettings(out var error))
            {
                MessageBox.Show(error, "Invalid Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save browser settings
            _appSettings.BrowserSettings.Headless = Headless;
            _appSettings.BrowserSettings.ViewportWidth = ViewportWidth;
            _appSettings.BrowserSettings.ViewportHeight = ViewportHeight;

            // Save crawl settings
            _appSettings.CrawlSettings = new CrawlSettings
            {
                ConcurrentRequests = ConcurrentRequests,
                TimeoutSeconds = TimeoutSeconds,
                MaxCrawlDepth = MaxCrawlDepth,
                MaxUrlsToCrawl = MaxUrlsToCrawl,
                RespectRobotsTxt = RespectRobotsTxt,
                UseSitemapXml = UseSitemapXml,
                CrawlDelaySeconds = CrawlDelaySeconds,
                MemoryLimitMb = MemoryLimitMb
            };

            await _appSettings.SaveAsync();

            _logger.LogInformation("Settings saved successfully");

            if (PluginsChanged)
            {
                MessageBox.Show(
                    "Settings saved. Please restart the application for plugin changes to take effect.",
                    "Restart Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _dialog.DialogResult = true;
            _dialog.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("Settings dialog cancelled");
        _dialog.DialogResult = false;
        _dialog.Close();
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        var result = MessageBox.Show(
            "Reset all settings to defaults?",
            "Restore Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Restore default crawl settings
            ConcurrentRequests = 2;
            TimeoutSeconds = 30;
            MaxCrawlDepth = 5;
            MaxUrlsToCrawl = 10000;
            RespectRobotsTxt = true;
            UseSitemapXml = true;
            CrawlDelaySeconds = 1.0;

            // Restore default browser settings
            Headless = true;
            ViewportWidth = 1920;
            ViewportHeight = 1080;
            MemoryLimitMb = 1536;

            _logger.LogInformation("Settings restored to defaults");
        }
    }

    private bool ValidateSettings(out string error)
    {
        if (ConcurrentRequests < 1 || ConcurrentRequests > 10)
        {
            error = "Concurrent requests must be between 1 and 10.";
            return false;
        }

        if (TimeoutSeconds < 5 || TimeoutSeconds > 300)
        {
            error = "Timeout must be between 5 and 300 seconds.";
            return false;
        }

        if (MaxCrawlDepth < 1 || MaxCrawlDepth > 20)
        {
            error = "Max crawl depth must be between 1 and 20.";
            return false;
        }

        if (MaxUrlsToCrawl < 1)
        {
            error = "Max URLs to crawl must be at least 1.";
            return false;
        }

        if (CrawlDelaySeconds < 0)
        {
            error = "Crawl delay cannot be negative.";
            return false;
        }

        if (ViewportWidth < 320 || ViewportWidth > 3840)
        {
            error = "Viewport width must be between 320 and 3840.";
            return false;
        }

        if (ViewportHeight < 240 || ViewportHeight > 2160)
        {
            error = "Viewport height must be between 240 and 2160.";
            return false;
        }

        if (MemoryLimitMb < 512)
        {
            error = "Memory limit must be at least 512 MB.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public class PluginInfo : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }
}

