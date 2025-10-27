using System;
using System.Threading;
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
    private CancellationTokenSource? _navigationCts;
    private Task? _navigationTask;

    [ObservableProperty]
    private int _urlsCrawled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
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
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isCrawling;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _recentActivity = string.Empty;

    [ObservableProperty]
    private string _pauseResumeButtonText = "Pause Crawl";

    [ObservableProperty]
    private string _pauseResumeButtonIcon = "\uE769"; // Pause icon

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool ShowEmptyState => TotalDiscovered == 0 && !IsCrawling;

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
            StatusMessage = "Starting crawl...";
            IsCrawling = true;
            await _crawlEngine.StartCrawlAsync(projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start crawl");
            StatusMessage = "Failed to start crawl";
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
            IsPaused = false;

            // Prompt user to view findings from partial results
            var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.MessageBox.Show(
                    "The crawl has been stopped. Would you like to view the findings from the URLs that were crawled?",
                    "View Findings",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question));

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _logger.LogInformation("Navigating to findings after stopping crawl");
                _navigationService.NavigateTo<Views.FindingsView>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop crawl");
        }
    }

    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        if (!IsCrawling)
            return;

        try
        {
            if (IsPaused)
            {
                _logger.LogInformation("Resuming crawl");
                await _crawlEngine.ResumeCrawlAsync();
                RecentActivity = "Crawl resumed";
            }
            else
            {
                _logger.LogInformation("Pausing crawl");
                await _crawlEngine.PauseCrawlAsync();
                RecentActivity = "Crawl paused";
            }

            UpdatePauseResumeState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause/resume crawl");
        }
    }

    private void UpdatePauseResumeState()
    {
        IsPaused = _crawlEngine.IsPaused;
        
        if (IsPaused)
        {
            PauseResumeButtonText = "Resume Crawl";
            PauseResumeButtonIcon = "\uE768"; // Play icon
        }
        else
        {
            PauseResumeButtonText = "Pause Crawl";
            PauseResumeButtonIcon = "\uE769"; // Pause icon
        }
    }

    private void OnProgressUpdated(object? sender, CrawlProgressEventArgs e)
    {
        // Use BeginInvoke for fire-and-forget to avoid blocking the progress reporter thread
        System.Windows.Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            var wasCrawling = IsCrawling;
            
            UrlsCrawled = e.UrlsCrawled;
            TotalDiscovered = e.TotalDiscovered;
            QueueSize = Math.Max(0, e.QueueSize); // Ensure queue size is never negative
            ActiveWorkers = e.ActiveWorkers;
            ErrorCount = e.ErrorCount;
            ElapsedTime = e.Elapsed.ToString(@"hh\:mm\:ss");

            if (TotalDiscovered > 0)
            {
                // Calculate progress and cap at 100% to prevent display issues
                var calculatedProgress = (double)UrlsCrawled / TotalDiscovered * 100;
                ProgressPercentage = Math.Min(100.0, calculatedProgress);
            }

            if (!string.IsNullOrEmpty(e.LastCrawledUrl))
            {
                RecentActivity = $"Last crawled: {e.LastCrawledUrl}";
            }
            
            // Update status message based on crawl state
            if (_crawlEngine.IsCrawling)
            {
                if (ActiveWorkers > 0)
                {
                    StatusMessage = $"Crawling... {ActiveWorkers} worker{(ActiveWorkers == 1 ? "" : "s")} active";
                }
                else if (QueueSize > 0)
                {
                    StatusMessage = "Processing queue...";
                }
                else if (UrlsCrawled == 0)
                {
                    StatusMessage = "Discovering URLs...";
                }
                else
                {
                    StatusMessage = "Crawling...";
                }
            }
            else
            {
                StatusMessage = string.Empty;
            }

            IsCrawling = _crawlEngine.IsCrawling;
            UpdatePauseResumeState();
            
            // If crawl just finished, check if it was successful
            if (wasCrawling && !IsCrawling)
            {
                // Check if crawl was successful (at least some URLs crawled without all being errors)
                int successfulCrawls = UrlsCrawled - ErrorCount;
                
                if (UrlsCrawled == 0 || (UrlsCrawled == 1 && ErrorCount == 1))
                {
                    // Complete failure - base URL couldn't be crawled
                    _logger.LogWarning("✗ Crawl failed! Could not crawl any URLs. {ErrorCount} errors", ErrorCount);
                    RecentActivity = $"✗ Crawl failed! Could not reach the base URL. Please check the URL and try again.";
                }
                else
                {
                    // At least some URLs were crawled successfully
                    _logger.LogInformation("✓ Crawl completed! Crawled {UrlsCrawled} URLs, discovered {TotalDiscovered} total, {ErrorCount} errors", 
                        UrlsCrawled, TotalDiscovered, ErrorCount);
                    
                    RecentActivity = $"✓ Crawl completed! Crawled {UrlsCrawled} URLs ({successfulCrawls} successful, {ErrorCount} errors) in {ElapsedTime}. Navigating to results...";
                    
                    // Navigate to Findings view after a short delay to show completion message
                    // Cancel any pending navigation first
                    if (_navigationTask != null)
                    {
                        _navigationCts?.Cancel();
                        try
                        {
                            await _navigationTask; // Wait for previous navigation to complete
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when cancelling
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Previous navigation task threw exception");
                        }
                    }
                    
                    _navigationCts?.Dispose();
                    _navigationCts = new CancellationTokenSource();
                    var cts = _navigationCts; // Capture to avoid closure issues
                    
                    // Track the navigation task so we can properly await/cancel it
                    _navigationTask = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2000, cts.Token); // Show completion message for 2 seconds
                            
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    _navigationService.NavigateTo<Views.FindingsView>();
                                    _logger.LogInformation("Navigated to Findings view after crawl completion");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to navigate to Findings view after crawl completion");
                                }
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogDebug("Navigation cancelled");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error in navigation task");
                        }
                    }, cts.Token);
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _crawlEngine.ProgressUpdated -= OnProgressUpdated;
        
        // Cancel any pending navigation task
        _navigationCts?.Cancel();
        
        // Don't wait for the navigation task to complete - let it finish or be cancelled naturally
        // Waiting can cause timeout warnings during disposal, especially during view transitions
        // The task will clean up on its own when cancelled
        
        _navigationCts?.Dispose();
        _disposed = true;
    }
}

