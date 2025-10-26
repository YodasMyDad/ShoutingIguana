using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Configuration;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Core.Services.NuGet;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IAppSettingsService _appSettings;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IProxyTestService _proxyTestService;
    private readonly IFeedConfigurationService _feedConfigService;
    private readonly IServiceProvider _serviceProvider;
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

    // Network/Proxy settings
    [ObservableProperty] private bool _proxyEnabled;
    [ObservableProperty] private int _selectedProxyType; // 0=Http, 1=Https, 2=Socks5  (ProxyType enum - 1)
    [ObservableProperty] private string _proxyServer = string.Empty;
    [ObservableProperty] private int _proxyPort = 8080;
    [ObservableProperty] private bool _proxyRequiresAuth;
    [ObservableProperty] private string _proxyUsername = string.Empty;
    [ObservableProperty] private string _proxyPassword = string.Empty; // In memory only, encrypted on save
    [ObservableProperty] private string _proxyBypassList = string.Empty; // Comma-separated patterns
    [ObservableProperty] private int _connectionTimeoutSeconds = 30;
    [ObservableProperty] private string _proxyTestResult = string.Empty;
    [ObservableProperty] private bool _isTestingProxy;

    // Plugin settings
    [ObservableProperty] private ObservableCollection<PluginInfo> _plugins = [];
    [ObservableProperty] private bool _pluginsChanged;

    // Data settings
    [ObservableProperty] private string _projectStoragePath = string.Empty;
    [ObservableProperty] private int _logRetentionDays = 30;
    [ObservableProperty] private int _checkpointInterval = 50;

    // Feed settings
    [ObservableProperty] private ObservableCollection<FeedInfo> _feeds = [];

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        IAppSettingsService appSettings,
        IPluginRegistry pluginRegistry,
        IProxyTestService proxyTestService,
        IFeedConfigurationService feedConfigService,
        IServiceProvider serviceProvider,
        Window dialog)
    {
        _logger = logger;
        _appSettings = appSettings;
        _pluginRegistry = pluginRegistry;
        _proxyTestService = proxyTestService;
        _feedConfigService = feedConfigService;
        _serviceProvider = serviceProvider;
        _dialog = dialog;

        LoadSettings();
        LoadPlugins();
        LoadDataSettings();
        LoadFeeds();
    }

    private void LoadSettings()
    {
        // Load browser settings
        Headless = _appSettings.BrowserSettings.Headless;
        ViewportWidth = _appSettings.BrowserSettings.ViewportWidth;
        ViewportHeight = _appSettings.BrowserSettings.ViewportHeight;

        // Load crawl settings from app settings
        var crawlSettings = _appSettings.CrawlSettings;
        ConcurrentRequests = crawlSettings.ConcurrentRequests;
        TimeoutSeconds = crawlSettings.TimeoutSeconds;
        MaxCrawlDepth = crawlSettings.MaxCrawlDepth;
        MaxUrlsToCrawl = crawlSettings.MaxUrlsToCrawl;
        RespectRobotsTxt = crawlSettings.RespectRobotsTxt;
        UseSitemapXml = crawlSettings.UseSitemapXml;
        CrawlDelaySeconds = crawlSettings.CrawlDelaySeconds;
        MemoryLimitMb = crawlSettings.MemoryLimitMb;
        ConnectionTimeoutSeconds = crawlSettings.ConnectionTimeoutSeconds;
        
        // Load proxy settings
        var proxy = crawlSettings.GlobalProxy;
        ProxyEnabled = proxy.Enabled;
        SelectedProxyType = proxy.Type == ProxyType.None ? 0 : (int)proxy.Type - 1; // Adjust for UI (0=Http, 1=Https, 2=Socks5)
        ProxyServer = proxy.Server;
        ProxyPort = proxy.Port;
        ProxyRequiresAuth = proxy.RequiresAuthentication;
        ProxyUsername = proxy.Username;
        ProxyPassword = proxy.GetPassword(); // Decrypt for editing
        ProxyBypassList = string.Join(", ", proxy.BypassList);

        _logger.LogDebug("Settings loaded");
    }

    private void LoadDataSettings()
    {
        // Load data settings
        ProjectStoragePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoutingIguana",
            "projects");
        
        LogRetentionDays = 30; // Default
        CheckpointInterval = 50; // Default
        
        _logger.LogDebug("Data settings loaded");
    }

    private async void LoadFeeds()
    {
        try
        {
            // Load feeds from configuration service
            var feeds = await _feedConfigService.GetFeedsAsync();
            
            Feeds.Clear();
            foreach (var feed in feeds)
            {
                Feeds.Add(new FeedInfo
                {
                    Name = feed.Name,
                    Url = feed.Url,
                    IsEnabled = feed.Enabled,
                    IsDefault = feed.Name.Equals("nuget.org", StringComparison.OrdinalIgnoreCase)
                });
            }

            _logger.LogDebug("Loaded {Count} feeds from configuration", Feeds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading feeds");
            
            // Fallback to default
            Feeds.Add(new FeedInfo
            {
                Name = "NuGet.org",
                Url = "https://api.nuget.org/v3/index.json",
                IsEnabled = true,
                IsDefault = true
            });
        }
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
            var proxySettings = new ProxySettings
            {
                Enabled = ProxyEnabled,
                Type = (ProxyType)(SelectedProxyType + 1), // Adjust from UI (0=Http, 1=Https, 2=Socks5) to enum
                Server = ProxyServer,
                Port = ProxyPort,
                RequiresAuthentication = ProxyRequiresAuth,
                Username = ProxyUsername,
                BypassList = ProxyBypassList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList()
            };
            proxySettings.SetPassword(ProxyPassword); // Encrypt
            
            _appSettings.CrawlSettings = new CrawlSettings
            {
                ConcurrentRequests = ConcurrentRequests,
                TimeoutSeconds = TimeoutSeconds,
                MaxCrawlDepth = MaxCrawlDepth,
                MaxUrlsToCrawl = MaxUrlsToCrawl,
                RespectRobotsTxt = RespectRobotsTxt,
                UseSitemapXml = UseSitemapXml,
                CrawlDelaySeconds = CrawlDelaySeconds,
                MemoryLimitMb = MemoryLimitMb,
                ConnectionTimeoutSeconds = ConnectionTimeoutSeconds,
                GlobalProxy = proxySettings
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

            // Restore default data settings
            LogRetentionDays = 30;
            CheckpointInterval = 50;

            _logger.LogInformation("Settings restored to defaults");
        }
    }

    [RelayCommand]
    private void ReloadPlugins()
    {
        try
        {
            _logger.LogInformation("Reloading plugins...");
            
            // Reload plugins from registry
            Plugins.Clear();
            LoadPlugins();
            
            MessageBox.Show(
                $"Plugins reloaded successfully. Found {Plugins.Count} plugin(s).",
                "Plugins Reloaded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload plugins");
            MessageBox.Show($"Failed to reload plugins: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void BrowseStoragePath()
    {
        MessageBox.Show(
            "Storage path customization coming soon.",
            "Coming Soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task TestProxyAsync()
    {
        if (!ProxyEnabled)
        {
            ProxyTestResult = "Proxy is disabled. Enable it first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProxyServer))
        {
            ProxyTestResult = "Proxy server is required.";
            return;
        }

        IsTestingProxy = true;
        ProxyTestResult = "Testing connection...";

        try
        {
            var proxySettings = new ProxySettings
            {
                Enabled = true,
                Type = (ProxyType)(SelectedProxyType + 1), // Adjust from UI to enum
                Server = ProxyServer,
                Port = ProxyPort,
                RequiresAuthentication = ProxyRequiresAuth,
                Username = ProxyUsername
            };
            proxySettings.SetPassword(ProxyPassword);

            var result = await _proxyTestService.TestConnectionAsync(proxySettings);
            ProxyTestResult = result.Message;

            _logger.LogInformation("Proxy test completed: {Success}", result.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy test failed");
            ProxyTestResult = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsTestingProxy = false;
        }
    }

    [RelayCommand]
    private async Task OptimizeDatabaseAsync()
    {
        try
        {
            var result = MessageBox.Show(
                "This will optimize the database by reclaiming unused space and rebuilding indices.\n\n" +
                "This may take a few moments. Continue?",
                "Optimize Database",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _logger.LogInformation("Optimizing database...");

            // Use reflection to execute VACUUM and ANALYZE on the database
            using var scope = _serviceProvider.CreateScope();
            
            // Get the DbContext through the IShoutingIguanaDbContext
            var dbContextType = Type.GetType("ShoutingIguana.Data.IShoutingIguanaDbContext, ShoutingIguana.Data");
            if (dbContextType == null)
            {
                MessageBox.Show("Unable to access database context.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dbContext = scope.ServiceProvider.GetService(dbContextType);
            if (dbContext == null)
            {
                MessageBox.Show("Database context not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get the Database property
            var databaseProperty = dbContextType.GetProperty("Database");
            if (databaseProperty == null)
            {
                MessageBox.Show("Unable to access database.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var database = databaseProperty.GetValue(dbContext);
            if (database == null)
            {
                MessageBox.Show("Database not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Execute VACUUM
            var optimizationExecuted = false;
            var execSqlMethod = database.GetType().GetMethod("ExecuteSqlRaw", new[] { typeof(string), typeof(object[]) });
            
            if (execSqlMethod != null)
            {
                _logger.LogInformation("Executing VACUUM...");
                execSqlMethod.Invoke(database, new object[] { "VACUUM;", Array.Empty<object>() });
                
                _logger.LogInformation("Executing ANALYZE...");
                execSqlMethod.Invoke(database, new object[] { "ANALYZE;", Array.Empty<object>() });
                
                optimizationExecuted = true;
            }
            else
            {
                // Try alternative method signature (async)
                execSqlMethod = database.GetType().GetMethod("ExecuteSqlRawAsync", new[] { typeof(string), typeof(object[]), typeof(CancellationToken) });
                if (execSqlMethod != null)
                {
                    _logger.LogInformation("Executing VACUUM...");
                    var vacuumTask = (Task)execSqlMethod.Invoke(database, new object[] { "VACUUM;", Array.Empty<object>(), CancellationToken.None })!;
                    await vacuumTask.ConfigureAwait(false);
                    
                    _logger.LogInformation("Executing ANALYZE...");
                    var analyzeTask = (Task)execSqlMethod.Invoke(database, new object[] { "ANALYZE;", Array.Empty<object>(), CancellationToken.None })!;
                    await analyzeTask.ConfigureAwait(false);
                    
                    optimizationExecuted = true;
                }
            }

            if (!optimizationExecuted)
            {
                _logger.LogWarning("Database optimization could not be executed - ExecuteSqlRaw method not found");
                MessageBox.Show(
                    "Unable to execute database optimization. The database method is not available.",
                    "Optimization Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _logger.LogInformation("Database optimization completed successfully");
            MessageBox.Show(
                "Database has been optimized successfully.\n\n" +
                "• Unused space reclaimed\n" +
                "• Indices rebuilt\n" +
                "• Query performance improved",
                "Optimization Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize database");
            MessageBox.Show($"Failed to optimize database: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void AddFeed()
    {
        Window? dialog = null;
        try
        {
            // Simple inline dialog for adding a feed
            dialog = new Window
            {
                Title = "Add NuGet Feed",
                Width = 500,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _dialog,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(20)
            };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            // Name field
            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Feed Name:",
                Margin = new Thickness(0, 0, 0, 8)
            };
            var nameTextBox = new System.Windows.Controls.TextBox
            {
                Margin = new Thickness(0, 0, 0, 16)
            };
            System.Windows.Controls.Grid.SetRow(nameLabel, 0);
            System.Windows.Controls.Grid.SetRow(nameTextBox, 0);
            nameTextBox.Margin = new Thickness(0, 20, 0, 16);

            // URL field
            var urlLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Feed URL:",
                Margin = new Thickness(0, 0, 0, 8)
            };
            var urlTextBox = new System.Windows.Controls.TextBox
            {
                Margin = new Thickness(0, 20, 0, 16),
                Text = "https://"
            };
            System.Windows.Controls.Grid.SetRow(urlLabel, 1);
            System.Windows.Controls.Grid.SetRow(urlTextBox, 1);
            urlTextBox.Margin = new Thickness(0, 20, 0, 0);

            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(nameLabel);
            stack.Children.Add(nameTextBox);
            stack.Children.Add(urlLabel);
            stack.Children.Add(urlTextBox);
            System.Windows.Controls.Grid.SetRow(stack, 1);

            // Buttons
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            var addButton = new System.Windows.Controls.Button
            {
                Content = "Add",
                Width = 80,
                Height = 32
            };
            addButton.Click += async (_, _) =>
            {
                var name = nameTextBox.Text.Trim();
                var url = urlTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Feed name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    MessageBox.Show("Valid feed URL is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check for duplicate
                if (Feeds.Any(f => f.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("A feed with this URL already exists.", "Duplicate Feed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // Add feed using the configuration service (persists to disk)
                    var newFeed = new NuGetFeed
                    {
                        Name = name,
                        Url = url,
                        Enabled = true
                    };

                    await _feedConfigService.AddFeedAsync(newFeed);
                    
                    // Update UI collection
                    Feeds.Add(new FeedInfo
                    {
                        Name = name,
                        Url = url,
                        IsEnabled = true,
                        IsDefault = false
                    });
                    
                    _logger.LogInformation("Added and persisted custom feed: {Name} - {Url}", name, url);
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add feed");
                    MessageBox.Show($"Failed to add feed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(addButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 3);

            grid.Children.Add(stack);
            grid.Children.Add(buttonPanel);
            dialog.Content = grid;

            // ShowDialog is modal - blocks until closed, then WPF handles cleanup
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding feed");
            MessageBox.Show($"Failed to add feed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Ensure dialog is closed if exception occurred during setup
            dialog?.Close();
        }
    }

    [RelayCommand]
    private void RemoveFeed()
    {
        Window? dialog = null;
        try
        {
            // Find selected feed (would need SelectedFeed property, but we can work with the collection)
            // For now, let's use a simple approach - show dialog to select which feed to remove
            if (Feeds.Count == 1)
            {
                MessageBox.Show("Cannot remove the default feed. At least one feed must be configured.", "Cannot Remove", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var nonDefaultFeeds = Feeds.Where(f => !f.IsDefault).ToList();
            if (nonDefaultFeeds.Count == 0)
            {
                MessageBox.Show("No custom feeds to remove. The default NuGet.org feed cannot be removed.", "No Custom Feeds", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show selection dialog
            dialog = new Window
            {
                Title = "Remove Feed",
                Width = 450,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _dialog,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(20)
            };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = "Select feed to remove:",
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);

            var listBox = new System.Windows.Controls.ListBox
            {
                ItemsSource = nonDefaultFeeds,
                DisplayMemberPath = "Name"
            };
            System.Windows.Controls.Grid.SetRow(listBox, 1);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, _) => dialog.Close();

            var removeButton = new System.Windows.Controls.Button
            {
                Content = "Remove",
                Width = 80,
                Height = 32
            };
            removeButton.Click += async (_, _) =>
            {
                var selected = listBox.SelectedItem as FeedInfo;
                if (selected == null)
                {
                    MessageBox.Show("Please select a feed to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to remove the feed '{selected.Name}'?",
                    "Confirm Remove",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Remove from persistence first
                        await _feedConfigService.RemoveFeedAsync(selected.Name);
                        
                        // Then update UI
                        Feeds.Remove(selected);
                        _logger.LogInformation("Removed and persisted feed deletion: {Name}", selected.Name);
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove feed");
                        MessageBox.Show($"Failed to remove feed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(removeButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(listBox);
            grid.Children.Add(buttonPanel);
            dialog.Content = grid;

            // ShowDialog is modal - blocks until closed, then WPF handles cleanup
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing feed");
            MessageBox.Show($"Failed to remove feed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Ensure dialog is closed if exception occurred during setup
            dialog?.Close();
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

        // Validate proxy settings if enabled
        if (ProxyEnabled)
        {
            if (string.IsNullOrWhiteSpace(ProxyServer))
            {
                error = "Proxy server is required when proxy is enabled.";
                return false;
            }

            if (ProxyPort < 1 || ProxyPort > 65535)
            {
                error = "Proxy port must be between 1 and 65535.";
                return false;
            }

            if (ProxyRequiresAuth && string.IsNullOrWhiteSpace(ProxyUsername))
            {
                error = "Proxy username is required when authentication is enabled.";
                return false;
            }
        }

        if (ConnectionTimeoutSeconds < 5 || ConnectionTimeoutSeconds > 300)
        {
            error = "Connection timeout must be between 5 and 300 seconds.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}


