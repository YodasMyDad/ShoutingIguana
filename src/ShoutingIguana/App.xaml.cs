using System;
using System.Collections.Generic;
using System.IO;
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

                    // Core Services
                    services.AddSingleton<IRobotsService, RobotsService>();
                    services.AddSingleton<ILinkExtractor, LinkExtractor>();
                    services.AddSingleton<ICrawlEngine, CrawlEngine>();

                    // Application Services
                    services.AddSingleton<IProjectContext, ProjectContext>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<ICsvExportService, CsvExportService>();

                    // ViewModels
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<ProjectHomeViewModel>();
                    services.AddTransient<CrawlDashboardViewModel>();
                    services.AddTransient<FindingsViewModel>();

                    // Main Window
                    services.AddTransient<MainWindow>();
                })
                .Build();

            // Create and show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
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
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

