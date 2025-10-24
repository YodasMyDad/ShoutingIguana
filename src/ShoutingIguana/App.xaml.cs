using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Data;
using ShoutingIguana.Data.Repositories;
using ShoutingIguana.Services;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana;

public partial class App : Application
{
    private IHost? _host;
    private CancellationTokenSource? _startupCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Setup Serilog first
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoutingIguana",
            "logs",
            "shouting-iguana.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .WriteTo.Debug() // Write to Debug output window in Visual Studio/Rider
            .WriteTo.Console() // Write to console if running from terminal
            .CreateLogger();

        Log.Information("Shouting Iguana application starting...");

        try
        {
            // Build host with configuration
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Load appsettings.json
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // Logging - integrate Serilog
                    services.AddLogging(builder =>
                    {
                        builder.ClearProviders();
                        builder.AddSerilog(Log.Logger, dispose: true);
                    });

                    // HttpClient
                    services.AddHttpClient();

                    // Database
                    services.AddSingleton<ISqliteDbContextFactory, SqliteDbContextFactory>();
                    services.AddSingleton<ProjectDbContextProvider>();
                    services.AddSingleton<IProjectDbContextProvider>(sp => sp.GetRequiredService<ProjectDbContextProvider>());
                    
                    // IShoutingIguanaDbContext - scoped per operation, resolved lazily when scope is created
                    services.AddScoped<IShoutingIguanaDbContext>(sp =>
                    {
                        var provider = sp.GetRequiredService<IProjectDbContextProvider>();
                        return provider.GetDbContext();
                    });

                    // Repositories
                    services.AddScoped<IProjectRepository, ProjectRepository>();
                    services.AddScoped<IUrlRepository, UrlRepository>();
                    services.AddScoped<ICrawlQueueRepository, CrawlQueueRepository>();
                    services.AddScoped<ILinkRepository, LinkRepository>();
                    services.AddScoped<IFindingRepository, FindingRepository>();
                    services.AddScoped<IRedirectRepository, RedirectRepository>();
                    services.AddScoped<IImageRepository, ImageRepository>();

                    // Core Services
                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<IRobotsService, RobotsService>();
                    services.AddSingleton<ILinkExtractor, LinkExtractor>();
                    services.AddSingleton<IPlaywrightService, PlaywrightService>();
                    services.AddSingleton<IPluginRegistry, PluginRegistry>();
                    services.AddSingleton<ICrawlEngine, CrawlEngine>();
                    services.AddScoped<PluginExecutor>();

                    // Application Services
                    services.AddSingleton<IProjectContext, ProjectContext>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<ICsvExportService, CsvExportService>();
                    services.AddSingleton<IExcelExportService, ExcelExportService>();

                    // ViewModels - Changed MainViewModel to Transient to ensure disposal
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<ProjectHomeViewModel>();
                    services.AddTransient<CrawlDashboardViewModel>();
                    services.AddTransient<FindingsViewModel>();
                    services.AddTransient<ExtensionsViewModel>();

                    // Main Window
                    services.AddTransient<MainWindow>();
                })
                .Build();

            // Load app settings synchronously - critical for startup
            var appSettingsService = _host.Services.GetRequiredService<IAppSettingsService>();
            appSettingsService.LoadAsync().GetAwaiter().GetResult();

            // Create main window and view model first
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = mainViewModel;
            
            // Initialize Playwright and plugins in background (non-blocking)
            _startupCts = new CancellationTokenSource();
            var playwrightService = _host.Services.GetRequiredService<IPlaywrightService>();
            var pluginRegistry = _host.Services.GetRequiredService<IPluginRegistry>();
            
            // Track initialization tasks so we can monitor for errors
            var playwrightTask = Task.Run(async () =>
            {
                try
                {
                    await playwrightService.InitializeAsync();
                    
                    // Auto-install browser on first run
                    if (!playwrightService.IsBrowserInstalled)
                    {
                        Log.Warning("Playwright browser not installed. Starting automatic installation...");
                        
                        try
                        {
                            await playwrightService.InstallBrowsersAsync();
                            Log.Information("Browser installation completed automatically");
                        }
                        catch (Exception installEx)
                        {
                            Log.Error(installEx, "Failed to auto-install browser");
                            
                            // Show error to user on UI thread
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show(
                                    $"Failed to install Playwright browser:\n\n{installEx.Message}\n\nSome features may not work correctly. You can try reinstalling from the Tools menu.",
                                    "Browser Installation Failed",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize Playwright");
                    
                    // Show error to user on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            $"Failed to initialize Playwright:\n\n{ex.Message}\n\nJavaScript rendering will not work. Please check the logs for details.",
                            "Playwright Initialization Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            }, _startupCts.Token);

            var pluginTask = Task.Run(async () =>
            {
                try
                {
                    await pluginRegistry.LoadPluginsAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load plugins");
                    
                    // Show error to user on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            $"Failed to load plugins:\n\n{ex.Message}\n\nAnalysis plugins will not be available. Please check the logs for details.",
                            "Plugin Loading Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            }, _startupCts.Token);
            
            // Monitor tasks for unhandled errors (but don't block startup)
            _ = Task.WhenAll(playwrightTask, pluginTask).ContinueWith(async t =>
            {
                if (t.IsFaulted)
                {
                    Log.Error(t.Exception, "Unhandled error in startup tasks");
                }
                
                // Initialization complete - enable UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    mainViewModel.IsInitializing = false;
                    Log.Information("Application initialization complete");
                });
            }, TaskScheduler.Default);

            // Show main window
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show($"Failed to start application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Shouting Iguana application shutting down...");
        
        // Cancel any pending startup tasks
        _startupCts?.Cancel();
        _startupCts?.Dispose();
        
        // Dispose Playwright resources with timeout
        if (_host != null)
        {
            try
            {
                var playwrightService = _host.Services.GetService<IPlaywrightService>();
                if (playwrightService != null)
                {
                    // Use Task.Run with timeout for async disposal in sync context
                    var disposeTask = Task.Run(async () => await playwrightService.DisposeAsync());
                    if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Log.Warning("Playwright disposal timed out after 5 seconds");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error disposing Playwright service");
            }
            
            _host.Dispose();
        }
        
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

