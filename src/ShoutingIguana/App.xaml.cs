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
using ShoutingIguana.Views;

namespace ShoutingIguana;

public partial class App : Application
{
    private IHost? _host;
    private CancellationTokenSource? _startupCts;
    
    /// <summary>
    /// Gets the application's service host for dependency injection.
    /// </summary>
    public IHost? ServiceHost => _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Setup global exception handlers
        SetupExceptionHandlers();

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
                    services.AddScoped<ICrawlCheckpointRepository, CrawlCheckpointRepository>();
                    services.AddScoped<ILinkRepository, LinkRepository>();
                    services.AddScoped<IFindingRepository, FindingRepository>();
                    services.AddScoped<IRedirectRepository, RedirectRepository>();
                    services.AddScoped<IImageRepository, ImageRepository>();
                    services.AddScoped<IHreflangRepository, HreflangRepository>();
                    services.AddScoped<IStructuredDataRepository, StructuredDataRepository>();
                    services.AddScoped<ICustomExtractionRuleRepository, CustomExtractionRuleRepository>();
                    services.AddScoped<IReportSchemaRepository, ReportSchemaRepository>();
                    services.AddScoped<IReportDataRepository, ReportDataRepository>();

                    // Core Services
                    services.AddSingleton<IAppSettingsService, AppSettingsService>();
                    services.AddSingleton<IRobotsService, RobotsService>();
                    services.AddSingleton<ISitemapService, SitemapService>();
                    services.AddSingleton<ILinkExtractor, LinkExtractor>();
                    services.AddSingleton<IPlaywrightService, PlaywrightService>();
                    services.AddSingleton<IPluginConfigurationService, PluginConfigurationService>();
                    services.AddSingleton<PluginSdk.IRepositoryAccessor, RepositoryAccessor>();
                    services.AddSingleton<IPluginRegistry, PluginRegistry>();
                    services.AddSingleton<ICrawlEngine, CrawlEngine>();
                    services.AddScoped<PluginExecutor>();
                    services.AddSingleton<IProxyTestService, ProxyTestService>();
                    services.AddSingleton<IListModeService, ListModeService>();
                    services.AddSingleton<ICustomExtractionService, CustomExtractionService>();
                    services.AddSingleton<FindingToReportAdapter>();
                    services.AddSingleton<ReportDataMigrationService>();

                    // NuGet Services
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.IFeedConfigurationService, ShoutingIguana.Core.Services.NuGet.FeedConfigurationService>();
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.IDependencyCache, ShoutingIguana.Core.Services.NuGet.DependencyCache>();
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.IDependencyResolver, ShoutingIguana.Core.Services.NuGet.DependencyResolver>();
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.IPackageSecurityService, ShoutingIguana.Core.Services.NuGet.PackageSecurityService>();
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.INuGetService, ShoutingIguana.Core.Services.NuGet.NuGetService>();
                    services.AddSingleton<ShoutingIguana.Core.Services.NuGet.IPackageManagerService, ShoutingIguana.Core.Services.NuGet.PackageManagerService>();

                    // Application Services
                    services.AddSingleton<IProjectContext, ProjectContext>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IExcelExportService, ExcelExportService>();
                    services.AddSingleton<IToastService, ToastService>();
                    services.AddSingleton<IStatusService, StatusService>();

                    // ViewModels - Changed MainViewModel to Transient to ensure disposal
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<ProjectHomeViewModel>();
                    services.AddTransient<CrawlDashboardViewModel>();
                    services.AddTransient<FindingsViewModel>();
                    services.AddTransient<PluginManagementViewModel>();

                    // Views
                    services.AddTransient<PluginManagementView>();

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

    private void SetupExceptionHandlers()
    {
        // Catch unhandled WPF UI thread exceptions
        DispatcherUnhandledException += (sender, e) =>
        {
            LogAndShowError(e.Exception, "An unexpected error occurred in the application");
            e.Handled = true; // Prevent app crash
        };

        // Catch unhandled exceptions from background threads
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            LogAndShowError(exception, "A critical error occurred");
            
            // If this is terminating, flush logs immediately
            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
            }
        };

        // Catch unhandled async exceptions (Task exceptions)
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            LogAndShowError(e.Exception, "An error occurred in a background operation");
            e.SetObserved(); // Prevent process termination
        };
    }

    private void LogAndShowError(Exception? exception, string userMessage)
    {
        if (exception == null)
        {
            return;
        }

        // Log the full exception
        Log.Fatal(exception, "Unhandled exception: {Message}", exception.Message);

        // Show user-friendly error dialog on UI thread
        Dispatcher.Invoke(() =>
        {
            try
            {
                var result = MessageBox.Show(
                    $"{userMessage}\n\nError: {exception.Message}\n\nWould you like to view the error logs?",
                    "Error - Shouting Iguana",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    // Open logs folder
                    var logFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ShoutingIguana",
                        "logs");

                    if (Directory.Exists(logFolder))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", logFolder);
                    }
                }
            }
            catch
            {
                // If showing the error dialog fails, just log it
                Log.Fatal("Failed to show error dialog for exception: {Exception}", exception);
            }
        });
    }
}

