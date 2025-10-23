using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class CrawlDashboardViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<CrawlDashboardViewModel> _logger;
    private readonly ICrawlEngine _crawlEngine;
    private readonly IProjectContext _projectContext;
    private readonly INavigationService _navigationService;
    private bool _disposed;

    [ObservableProperty]
    private int _urlsCrawled;

    [ObservableProperty]
    private int _totalDiscovered;

    [ObservableProperty]
    private int _queueSize;

    [ObservableProperty]
    private int _activeWorkers;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private string _elapsedTime = "00:00:00";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _isCrawling;

    [ObservableProperty]
    private string _recentActivity = string.Empty;

    public CrawlDashboardViewModel(
        ILogger<CrawlDashboardViewModel> logger, 
        ICrawlEngine crawlEngine,
        IProjectContext projectContext,
        INavigationService navigationService)
    {
        _logger = logger;
        _crawlEngine = crawlEngine;
        _projectContext = projectContext;
        _navigationService = navigationService;
        
        _crawlEngine.ProgressUpdated += OnProgressUpdated;
    }

    public async Task InitializeAsync(bool autoStart = false)
    {
        if (autoStart && !IsCrawling && _projectContext.HasOpenProject)
        {
            await StartCrawlAsync();
        }
    }

    [RelayCommand]
    private async Task StartCrawlAsync()
    {
        if (IsCrawling)
            return;

        if (!_projectContext.HasOpenProject)
        {
            _logger.LogWarning("Cannot start crawl: no project is open");
            return;
        }

        try
        {
            var projectId = _projectContext.CurrentProjectId!.Value;
            _logger.LogInformation("Starting crawl for project {ProjectId}", projectId);
            IsCrawling = true;
            await _crawlEngine.StartCrawlAsync(projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start crawl");
            IsCrawling = false;
        }
    }

    [RelayCommand]
    private async Task StopCrawlAsync()
    {
        if (!IsCrawling)
            return;

        try
        {
            _logger.LogInformation("Stopping crawl");
            await _crawlEngine.StopCrawlAsync();
            IsCrawling = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop crawl");
        }
    }

    private void OnProgressUpdated(object? sender, CrawlProgressEventArgs e)
    {
        // Use BeginInvoke for fire-and-forget to avoid blocking the progress reporter thread
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var wasCrawling = IsCrawling;
            
            UrlsCrawled = e.UrlsCrawled;
            TotalDiscovered = e.TotalDiscovered;
            QueueSize = e.QueueSize;
            ActiveWorkers = e.ActiveWorkers;
            ErrorCount = e.ErrorCount;
            ElapsedTime = e.Elapsed.ToString(@"hh\:mm\:ss");

            if (TotalDiscovered > 0)
            {
                ProgressPercentage = (double)UrlsCrawled / TotalDiscovered * 100;
            }

            if (!string.IsNullOrEmpty(e.LastCrawledUrl))
            {
                RecentActivity = $"Last crawled: {e.LastCrawledUrl}";
            }

            IsCrawling = _crawlEngine.IsCrawling;
            
            // If crawl just finished, show completion message and navigate to findings
            if (wasCrawling && !IsCrawling)
            {
                _logger.LogInformation("✓ Crawl completed! Crawled {UrlsCrawled} URLs, discovered {TotalDiscovered} total, {ErrorCount} errors", 
                    UrlsCrawled, TotalDiscovered, ErrorCount);
                
                RecentActivity = $"✓ Crawl completed! Crawled {UrlsCrawled} URLs in {ElapsedTime}. Navigating to results...";
                
                // Navigate to Findings view after a short delay to show completion message
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2000); // Show completion message for 2 seconds
                    
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            _navigationService.NavigateTo<ShoutingIguana.Views.FindingsView>();
                            _logger.LogInformation("Navigated to Findings view after crawl completion");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to navigate to Findings view after crawl completion");
                        }
                    });
                });
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _crawlEngine.ProgressUpdated -= OnProgressUpdated;
        _disposed = true;
    }
}

