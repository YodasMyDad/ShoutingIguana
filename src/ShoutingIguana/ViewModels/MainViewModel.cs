using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Services;
using ShoutingIguana.Views;

namespace ShoutingIguana.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;

    [ObservableProperty]
    private UserControl? _currentView;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _projectName = "No project loaded";

    public MainViewModel(
        ILogger<MainViewModel> logger, 
        INavigationService navigationService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _navigationService = navigationService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        
        _navigationService.NavigationRequested += OnNavigationRequested;
        _projectContext.ProjectChanged += OnProjectChanged;
        
        // Start with project home view
        _navigationService.NavigateTo<ProjectHomeView>();
        StatusMessage = "Ready";
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        ProjectName = _projectContext.HasOpenProject 
            ? _projectContext.CurrentProjectName ?? "Unknown Project"
            : "No project loaded";
    }

    private void OnNavigationRequested(object? sender, UserControl view)
    {
        CurrentView = view;
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        await NavigateToProjectHomeAsync();
        
        // Give UI time to load the view
        await Task.Delay(100);
        
        // Trigger the new project action in ProjectHomeViewModel
        if (CurrentView is ProjectHomeView projectHomeView)
        {
            if (projectHomeView.DataContext is ProjectHomeViewModel vm)
            {
                vm.NewProjectCommand.Execute(null);
            }
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        await NavigateToProjectHomeAsync();
        // Trigger the open dialog in ProjectHomeViewModel
        await Task.Delay(100); // Give UI time to load
        var projectHomeView = CurrentView as ProjectHomeView;
        if (projectHomeView?.DataContext is ProjectHomeViewModel vm)
        {
            await vm.OpenProjectCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task NavigateToProjectHomeAsync()
    {
        _navigationService.NavigateTo<ProjectHomeView>();
        StatusMessage = "Project Home";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NavigateToCrawlDashboardAsync()
    {
        _navigationService.NavigateTo<CrawlDashboardView>();
        StatusMessage = "Crawl Dashboard";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NavigateToFindingsAsync()
    {
        _navigationService.NavigateTo<FindingsView>();
        StatusMessage = "Findings";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        if (!_projectContext.HasOpenProject)
            return;

        try
        {
            var dbProvider = _serviceProvider.GetRequiredService<ShoutingIguana.Data.ProjectDbContextProvider>();
            dbProvider.CloseProject();
            _projectContext.CloseProject();
            
            await NavigateToProjectHomeAsync();
            _logger.LogInformation("Closed project");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close project");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to close project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private void Exit()
    {
        _logger.LogInformation("Exiting application");
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void ViewLogs()
    {
        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoutingIguana",
            "logs");

        if (System.IO.Directory.Exists(logPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", logPath);
        }
    }

    [RelayCommand]
    private void About()
    {
        MessageBox.Show(
            "Shouting Iguana\nVersion 1.0.0 (Stage 1)\n\nA professional web crawler and SEO analysis tool.",
            "About Shouting Iguana",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Refresh current view
        if (CurrentView is FindingsView findingsView)
        {
            if (findingsView.DataContext is FindingsViewModel findingsVm)
            {
                await findingsVm.RefreshCommand.ExecuteAsync(null);
            }
        }
        else if (CurrentView is CrawlDashboardView dashboardView)
        {
            // Dashboard updates in real-time, no explicit refresh needed
            StatusMessage = "Crawl dashboard updates automatically";
        }
        else if (CurrentView is ProjectHomeView projectHomeView)
        {
            if (projectHomeView.DataContext is ProjectHomeViewModel projectVm)
            {
                await projectVm.LoadAsync();
            }
        }
        
        StatusMessage = "Refreshed";
        _logger.LogInformation("View refreshed");
    }

    [RelayCommand]
    private async Task StartCrawlAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("Please open or create a project first", "No Project", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        // Navigate to crawl dashboard if not already there
        if (CurrentView is not CrawlDashboardView)
        {
            await NavigateToCrawlDashboardAsync();
            await Task.Delay(100); // Give UI time to load
        }

        // Start crawl
        var dashboardView = CurrentView as CrawlDashboardView;
        if (dashboardView?.DataContext is CrawlDashboardViewModel vm)
        {
            await vm.StartCrawlCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task StopCrawlAsync()
    {
        // Navigate to crawl dashboard if not already there
        if (CurrentView is not CrawlDashboardView)
        {
            await NavigateToCrawlDashboardAsync();
            await Task.Delay(100);
        }

        // Stop crawl
        var dashboardView = CurrentView as CrawlDashboardView;
        if (dashboardView?.DataContext is CrawlDashboardViewModel vm)
        {
            await vm.StopCrawlCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "No Project", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                "Are you sure you want to clear the crawl queue?",
                "Clear Queue",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question));

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                await queueRepo.ClearQueueAsync(_projectContext.CurrentProjectId!.Value);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Queue cleared successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                _logger.LogInformation("Cleared crawl queue for project {ProjectId}", _projectContext.CurrentProjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear queue");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to clear queue: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "No Project", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        // Navigate to findings view and trigger export
        if (CurrentView is not FindingsView)
        {
            await NavigateToFindingsAsync();
            await Task.Delay(100);
        }

        var findingsView = CurrentView as FindingsView;
        if (findingsView?.DataContext is FindingsViewModel vm)
        {
            await vm.ExportToCsvCommand.ExecuteAsync(null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _navigationService.NavigationRequested -= OnNavigationRequested;
        _projectContext.ProjectChanged -= OnProjectChanged;
        _disposed = true;
    }
}

