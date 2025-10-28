using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Data;
using ShoutingIguana.Services;
using ShoutingIguana.Views;

namespace ShoutingIguana.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlaywrightService _playwrightService;
    private readonly IToastService _toastService;
    private readonly ICrawlEngine _crawlEngine;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IStatusService _statusService;
    private readonly IAppSettingsService _appSettingsService;
    private bool _disposed;

    [ObservableProperty]
    private UserControl? _currentView;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _projectName = "No project loaded";

    [ObservableProperty]
    private string _browserStatusText = "Initializing...";

    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _hasOpenProject;

    [ObservableProperty]
    private string _pauseResumeMenuText = "Pause Crawl";

    [ObservableProperty]
    private string _pauseResumeIcon = "\uE769"; // Pause icon

    [ObservableProperty]
    private int _pluginCount;

    [ObservableProperty]
    private ObservableCollection<Core.Configuration.RecentProject> _recentProjects = new();

    /// <summary>
    /// Gets the collection of active toast notifications.
    /// </summary>
    public ObservableCollection<ToastViewModel> Toasts => _toastService.Toasts;

    public MainViewModel(
        ILogger<MainViewModel> logger, 
        INavigationService navigationService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider,
        IPlaywrightService playwrightService,
        IToastService toastService,
        ICrawlEngine crawlEngine,
        IPluginRegistry pluginRegistry,
        IStatusService statusService,
        IAppSettingsService appSettingsService)
    {
        _logger = logger;
        _navigationService = navigationService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        _playwrightService = playwrightService;
        _toastService = toastService;
        _crawlEngine = crawlEngine;
        _pluginRegistry = pluginRegistry;
        _statusService = statusService;
        _appSettingsService = appSettingsService;
        
        _navigationService.NavigationRequested += OnNavigationRequested;
        _projectContext.ProjectChanged += OnProjectChanged;
        _playwrightService.StatusChanged += OnBrowserStatusChanged;
        _crawlEngine.ProgressUpdated += OnCrawlProgressUpdated;
        _pluginRegistry.PluginLoaded += OnPluginChanged;
        _pluginRegistry.PluginUnloaded += OnPluginChanged;
        _statusService.StatusChanged += OnStatusChanged;
        
        // Set initial browser status
        UpdateBrowserStatus(_playwrightService.Status);
        
        // Initialize project state
        HasOpenProject = _projectContext.HasOpenProject;
        
        // Update pause/resume state
        UpdatePauseResumeState();
        
        // Update plugin count
        UpdatePluginCount();
        
        // Load recent projects
        LoadRecentProjects();
        
        // Start with project home view
        _navigationService.NavigateTo<ProjectHomeView>();
        StatusMessage = "Ready";
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        HasOpenProject = _projectContext.HasOpenProject;
        ProjectName = _projectContext.HasOpenProject 
            ? _projectContext.CurrentProjectName ?? "Unknown Project"
            : "No project loaded";
        
        // Refresh recent projects list when a project changes
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        // Load recent projects on a background thread to avoid blocking UI with File.Exists checks
        Task.Run(async () =>
        {
            try
            {
                var recentProjects = _appSettingsService.GetRecentProjects();
                
                // Validate that files still exist and remove invalid ones
                var validProjects = new System.Collections.Generic.List<Core.Configuration.RecentProject>();
                bool removedAny = false;
                
                foreach (var project in recentProjects)
                {
                    if (System.IO.File.Exists(project.FilePath))
                    {
                        validProjects.Add(project);
                    }
                    else
                    {
                        _appSettingsService.RemoveRecentProject(project.FilePath);
                        removedAny = true;
                        _logger.LogDebug("Removed non-existent project from recent list: {FilePath}", project.FilePath);
                    }
                }

                // Update UI collection on UI thread (ObservableCollection requires this)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RecentProjects.Clear();
                    foreach (var project in validProjects)
                    {
                        RecentProjects.Add(project);
                    }
                });

                // Save if we removed any invalid projects
                if (removedAny)
                {
                    await _appSettingsService.SaveAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent projects");
            }
        });
    }

    private void OnNavigationRequested(object? sender, UserControl view)
    {
        // Dispose old view's DataContext if it implements IDisposable
        if (CurrentView?.DataContext is IDisposable disposable)
        {
            try
            {
                var typeName = disposable.GetType().FullName;
                disposable.Dispose();
                _logger.LogDebug("Disposed previous view's DataContext of type {Type}", typeName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing previous view's DataContext");
            }
        }
        
        CurrentView = view;
    }

    private void OnBrowserStatusChanged(object? sender, BrowserStatusEventArgs e)
    {
        UpdateBrowserStatus(e.Status);
    }

    private void UpdateBrowserStatus(BrowserStatus status)
    {
        BrowserStatusText = status switch
        {
            BrowserStatus.NotInitialized => "Not Initialized",
            BrowserStatus.Initializing => "Initializing...",
            BrowserStatus.Installing => "Installing...",
            BrowserStatus.Ready => "✓ Ready",
            BrowserStatus.Error => "✗ Error",
            _ => "Unknown"
        };
    }

    private void OnCrawlProgressUpdated(object? sender, CrawlProgressEventArgs e)
    {
        // Update pause/resume state when crawl state changes - must be on UI thread
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            UpdatePauseResumeState();
        });
    }

    private void UpdatePauseResumeState()
    {
        if (_crawlEngine.IsPaused)
        {
            PauseResumeMenuText = "Resume Crawl";
            PauseResumeIcon = "\uE768"; // Play icon
        }
        else
        {
            PauseResumeMenuText = "Pause Crawl";
            PauseResumeIcon = "\uE769"; // Pause icon
        }
    }

    private void OnPluginChanged(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            UpdatePluginCount();
        });
    }

    private void UpdatePluginCount()
    {
        PluginCount = _pluginRegistry.LoadedPlugins.Count();
    }

    private void OnStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        // Check if a project is currently open
        if (_projectContext.HasOpenProject)
        {
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show(
                    "Starting a new project will close the current project.\n\nDo you want to continue?",
                    "Close Current Project",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question));

            if (result == MessageBoxResult.No)
            {
                return;
            }

            // Close the current project
            await CloseProjectAsync();
        }

        // Navigate to project home
        await NavigateToProjectHomeAsync();
        
        // Trigger the new project action in ProjectHomeViewModel
        // The view is already loaded since NavigateTo is synchronous
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
        var projectHomeView = CurrentView as ProjectHomeView;
        if (projectHomeView?.DataContext is ProjectHomeViewModel vm)
        {
            await vm.OpenProjectCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(Core.Configuration.RecentProject recentProject)
    {
        if (recentProject == null)
            return;

        try
        {
            // Validate file exists
            if (!System.IO.File.Exists(recentProject.FilePath))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"The project file could not be found:\n{recentProject.FilePath}\n\nIt may have been moved or deleted.",
                        "Project Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));

                // Remove from recent list
                _appSettingsService.RemoveRecentProject(recentProject.FilePath);
                await _appSettingsService.SaveAsync();
                LoadRecentProjects();
                return;
            }

            // Check if a project is currently open and close it
            if (_projectContext.HasOpenProject)
            {
                _logger.LogInformation("Closing current project to open recent project");
                await CloseProjectAsync();
            }

            // Switch to the selected database
            var dbProvider = _serviceProvider.GetRequiredService<ProjectDbContextProvider>();
            await dbProvider.SetProjectPathAsync(recentProject.FilePath);

            // Load project from database
            using var scope = _serviceProvider.CreateScope();
            var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var projects = await projectRepo.GetRecentProjectsAsync(1);
            var project = projects.FirstOrDefault();

            if (project == null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("No project found in the selected database.", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                dbProvider.CloseProject();
                return;
            }

            // Update project context
            _projectContext.OpenProject(recentProject.FilePath, project.Id, project.Name);

            // Update last opened time
            project.LastOpenedUtc = DateTime.UtcNow;
            await projectRepo.UpdateAsync(project);

            // Update recent projects list (this will move it to the top)
            _appSettingsService.AddRecentProject(project.Name, recentProject.FilePath);
            await _appSettingsService.SaveAsync();

            // Navigate to project home
            await NavigateToProjectHomeAsync();

            _logger.LogInformation("Opened recent project: {ProjectName} from {FilePath}", project.Name, recentProject.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open recent project from {FilePath}", recentProject.FilePath);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
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
            // Stop crawl if running before closing project
            if (_crawlEngine.IsCrawling)
            {
                _logger.LogInformation("Stopping active crawl before closing project");
                await _crawlEngine.StopCrawlAsync();
                
                // Give crawl time to stop gracefully
                await Task.Delay(500);
            }
            
            var projectId = _projectContext.CurrentProjectId;
            
            // Cleanup plugin data for this project
            if (projectId.HasValue)
            {
                var tasks = _pluginRegistry.RegisteredTasks;
                foreach (var task in tasks)
                {
                    try
                    {
                        task.CleanupProject(projectId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error cleaning up plugin task {TaskKey}", task.Key);
                    }
                }
                _logger.LogDebug("Cleaned up plugin data for project {ProjectId}", projectId.Value);
            }
            
            var dbProvider = _serviceProvider.GetRequiredService<ProjectDbContextProvider>();
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
        try
        {
            var aboutDialog = new AboutDialog(_serviceProvider)
            {
                Owner = Application.Current.MainWindow
            };
            aboutDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show About dialog");
            MessageBox.Show(
                "Shouting Iguana\nVersion 1.0.0 (Stage 3 MVP)\n\nA professional web crawler and SEO analysis tool.",
                "About Shouting Iguana",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
        else if (CurrentView is CrawlDashboardView)
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
        }

        var findingsView = CurrentView as FindingsView;
        if (findingsView?.DataContext is FindingsViewModel vm)
        {
            await vm.ExportToCsvCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
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
        }

        var findingsView = CurrentView as FindingsView;
        if (findingsView?.DataContext is FindingsViewModel vm)
        {
            await vm.ExportToExcelCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task NavigateToPluginManagementAsync()
    {
        _navigationService.NavigateTo<PluginManagementView>();
        StatusMessage = "Plugin Management";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ReinstallBrowsersAsync()
    {
        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                "This will reinstall Playwright browsers. This may take a few minutes.\n\nContinue?",
                "Reinstall Browsers",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question));

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                StatusMessage = "Installing browsers...";
                await _playwrightService.InstallBrowsersAsync();
                StatusMessage = "Browser installation complete";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Browsers installed successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                _logger.LogInformation("Browsers reinstalled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reinstall browsers");
                StatusMessage = "Browser installation failed";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to install browsers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    [RelayCommand]
    private void Options()
    {
        try
        {
            _logger.LogInformation("Opening settings dialog");
            var settingsDialog = new SettingsDialog(_serviceProvider)
            {
                Owner = Application.Current.MainWindow
            };
            
            var result = settingsDialog.ShowDialog();
            if (result == true)
            {
                _logger.LogInformation("Settings saved successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening settings dialog");
            MessageBox.Show($"Failed to open settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== Edit Menu Commands =====

    [RelayCommand]
    private void Copy()
    {
        try
        {
            // Delegate to current view's copy functionality
            if (CurrentView is FindingsView findingsView && findingsView.DataContext is FindingsViewModel findingsVm)
            {
                findingsVm.CopySelectedCommand.Execute(null);
            }
            else if (CurrentView is LinkGraphView linkGraphView && linkGraphView.DataContext is LinkGraphViewModel linkGraphVm)
            {
                linkGraphVm.CopySelectedCommand.Execute(null);
            }
            else
            {
                _logger.LogDebug("Copy not supported in current view");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing copy command");
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        try
        {
            // Delegate to current view's select all functionality
            if (CurrentView is FindingsView findingsView && findingsView.DataContext is FindingsViewModel findingsVm)
            {
                findingsVm.SelectAllCommand.Execute(null);
            }
            else if (CurrentView is LinkGraphView linkGraphView && linkGraphView.DataContext is LinkGraphViewModel linkGraphVm)
            {
                linkGraphVm.SelectAllCommand.Execute(null);
            }
            else
            {
                _logger.LogDebug("Select All not supported in current view");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing select all command");
        }
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        // Navigate to findings view if not already there
        if (CurrentView is not FindingsView)
        {
            await NavigateToFindingsAsync();
        }

        _logger.LogInformation("Find command executed - Findings view active");
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        // Navigate to findings view if not already there
        if (CurrentView is not FindingsView)
        {
            await NavigateToFindingsAsync();
        }

        // Clear filters on the selected tab
        if (CurrentView is FindingsView findingsView && findingsView.DataContext is FindingsViewModel findingsVm)
        {
            if (findingsVm.SelectedTab != null)
            {
                findingsVm.SelectedTab.SelectedSeverity = null;
                findingsVm.SelectedTab.SearchText = string.Empty;
                _logger.LogInformation("Cleared filters on tab: {TabName}", findingsVm.SelectedTab.DisplayName);
            }
            else
            {
                _logger.LogWarning("No tab selected to clear filters");
            }
        }
    }

    // ===== Navigation Commands =====

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "No Project", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        try
        {
            // If we're on project home view, trigger save
            if (CurrentView is ProjectHomeView projectHomeView && projectHomeView.DataContext is ProjectHomeViewModel projectHomeVm)
            {
                await projectHomeVm.SaveSettingsCommand.ExecuteAsync(null);
                _logger.LogInformation("Project settings saved");
            }
            else
            {
                // Navigate to project home and save
                await NavigateToProjectHomeAsync();
                
                if (CurrentView is ProjectHomeView phView && phView.DataContext is ProjectHomeViewModel phVm)
                {
                    await phVm.SaveSettingsCommand.ExecuteAsync(null);
                    _logger.LogInformation("Project settings saved");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving project");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to save project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task NavigateToLinkGraphAsync()
    {
        _navigationService.NavigateTo<LinkGraphView>();
        StatusMessage = "Link Graph";
        await Task.CompletedTask;
    }

    // ===== Pause/Resume Command =====

    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        if (!_crawlEngine.IsCrawling)
        {
            _logger.LogWarning("Cannot pause/resume: crawl is not running");
            return;
        }

        try
        {
            if (_crawlEngine.IsPaused)
            {
                _logger.LogInformation("Resuming crawl");
                await _crawlEngine.ResumeCrawlAsync();
                StatusMessage = "Crawl resumed";
            }
            else
            {
                _logger.LogInformation("Pausing crawl");
                await _crawlEngine.PauseCrawlAsync();
                StatusMessage = "Crawl paused";
            }

            UpdatePauseResumeState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause/resume crawl");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to pause/resume crawl: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    // ===== Destructive Actions =====

    [RelayCommand]
    private async Task ResetProjectDataAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "No Project", MessageBoxButton.OK, MessageBoxImage.Information));
            return;
        }

        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                "This will delete ALL crawl data including URLs, findings, links, and images.\n\nThis action cannot be undone. Continue?",
                "Reset Project Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning));

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var findingRepo = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
                var imageRepo = scope.ServiceProvider.GetRequiredService<IImageRepository>();
                var redirectRepo = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();
                var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();

                var projectId = _projectContext.CurrentProjectId!.Value;

                // Clear all data that has DeleteByProjectIdAsync
                await queueRepo.ClearQueueAsync(projectId);
                await findingRepo.DeleteByProjectIdAsync(projectId);
                await imageRepo.DeleteByProjectIdAsync(projectId);
                await redirectRepo.DeleteByProjectIdAsync(projectId);

                _logger.LogInformation("Reset project data for project {ProjectId}", projectId);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Project data has been reset (partial - URLs and Links retained)", "Success", MessageBoxButton.OK, MessageBoxImage.Information));

                // Navigate back to project home
                await NavigateToProjectHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset project data");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to reset project data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    // ===== Tools Menu Commands =====

    [RelayCommand]
    private void ImportUrlList()
    {
        if (!HasOpenProject || _projectContext.CurrentProjectId == null)
        {
            _toastService.ShowWarning("No Project", "Please open a project first");
            return;
        }

        try
        {
            var dialog = new ListModeImportDialog(_serviceProvider, _projectContext.CurrentProjectId.Value);
            dialog.Owner = Application.Current.MainWindow;
            var result = dialog.ShowDialog();

            if (result == true)
            {
                _logger.LogInformation("URL list imported successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening list import dialog");
            _toastService.ShowError("Error", "Failed to open import dialog");
        }
    }

    [RelayCommand]
    private void CustomExtraction()
    {
        if (!HasOpenProject || _projectContext.CurrentProjectId == null)
        {
            _toastService.ShowWarning("No Project", "Please open a project first");
            return;
        }

        try
        {
            var dialog = new CustomExtractionDialog(_serviceProvider, _projectContext.CurrentProjectId.Value);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            
            _logger.LogInformation("Custom Extraction dialog closed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening custom extraction dialog");
            _toastService.ShowError("Error", "Failed to open custom extraction dialog");
        }
    }

    [RelayCommand]
    private void TestProxy()
    {
        // Open settings dialog on Network tab
        try
        {
            var settingsDialog = new SettingsDialog(_serviceProvider);
            settingsDialog.Owner = Application.Current.MainWindow;
            settingsDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening settings dialog");
            _toastService.ShowError("Error", "Failed to open settings");
        }
    }

    // ===== Help Menu Commands =====

    [RelayCommand]
    private void Documentation()
    {
        try
        {
            var docsUrl = "https://github.com/yourusername/ShoutingIguana/wiki";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = docsUrl,
                UseShellExecute = true
            });
            _logger.LogInformation("Opened documentation in browser");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open documentation");
            MessageBox.Show(
                "Documentation is available at: https://github.com/yourusername/ShoutingIguana/wiki",
                "Documentation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void PluginDevGuide()
    {
        try
        {
            var guideUrl = "https://github.com/yourusername/ShoutingIguana/wiki/Plugin-Development";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = guideUrl,
                UseShellExecute = true
            });
            _logger.LogInformation("Opened plugin dev guide in browser");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open plugin dev guide");
            MessageBox.Show(
                "Plugin Development Guide is available at: https://github.com/yourusername/ShoutingIguana/wiki/Plugin-Development",
                "Plugin Development Guide",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdatesAsync()
    {
        try
        {
            _logger.LogInformation("Checking for updates...");
            StatusMessage = "Checking for updates...";

            // Check GitHub API for latest release
            const string currentVersion = "1.0.0";
            const string githubApiUrl = "https://api.github.com/repos/yourusername/ShoutingIguana/releases/latest";
            
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ShoutingIguana");
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = await httpClient.GetAsync(githubApiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {StatusCode}", response.StatusCode);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        "Unable to check for updates. Please visit the GitHub releases page manually.",
                        "Update Check",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
                StatusMessage = "Update check unavailable";
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Safely extract properties with fallbacks
            var latestVersionTag = currentVersion;
            var releaseName = "Latest Release";
            var releaseUrl = string.Empty;
            
            if (root.TryGetProperty("tag_name", out var tagElement))
            {
                latestVersionTag = tagElement.GetString()?.TrimStart('v') ?? currentVersion;
            }
            
            if (root.TryGetProperty("name", out var nameElement))
            {
                releaseName = nameElement.GetString() ?? "Latest Release";
            }
            
            if (root.TryGetProperty("html_url", out var urlElement))
            {
                releaseUrl = urlElement.GetString() ?? string.Empty;
            }
            
            _logger.LogInformation("Latest version: {LatestVersion}, Current: {CurrentVersion}", latestVersionTag, currentVersion);

            // Simple version comparison (assumes semantic versioning)
            if (IsNewerVersion(currentVersion, latestVersionTag))
            {
                var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"A new version is available!\n\n" +
                        $"Current version: {currentVersion}\n" +
                        $"Latest version: {latestVersionTag}\n" +
                        $"Release: {releaseName}\n\n" +
                        $"Would you like to download the update?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information));

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(releaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }

                StatusMessage = "Update available";
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"You are running the latest version of Shouting Iguana (v{currentVersion}).",
                        "No Updates Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));

                StatusMessage = "Up to date";
            }

            _logger.LogInformation("Update check complete");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error checking for updates");
            StatusMessage = "Update check failed";
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show(
                    "Unable to check for updates. Please check your internet connection.",
                    "Network Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            StatusMessage = "Update check failed";
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to check for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    /// <summary>
    /// Compares two semantic version strings.
    /// </summary>
    /// <returns>True if newVersion is newer than currentVersion.</returns>
    private static bool IsNewerVersion(string currentVersion, string newVersion)
    {
        try
        {
            var current = Version.Parse(currentVersion);
            var latest = Version.Parse(newVersion);
            return latest > current;
        }
        catch
        {
            // If parsing fails, assume no update
            return false;
        }
    }

    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            var issuesUrl = "https://github.com/yourusername/ShoutingIguana/issues/new";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = issuesUrl,
                UseShellExecute = true
            });
            _logger.LogInformation("Opened GitHub issues in browser");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open GitHub issues");
            MessageBox.Show(
                "Report issues at: https://github.com/yourusername/ShoutingIguana/issues",
                "Report Issue",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            if (window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Normal;
                _logger.LogInformation("Exited full screen");
            }
            else
            {
                window.WindowState = WindowState.Maximized;
                _logger.LogInformation("Entered full screen");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _navigationService.NavigationRequested -= OnNavigationRequested;
        _projectContext.ProjectChanged -= OnProjectChanged;
        _playwrightService.StatusChanged -= OnBrowserStatusChanged;
        _crawlEngine.ProgressUpdated -= OnCrawlProgressUpdated;
        _pluginRegistry.PluginLoaded -= OnPluginChanged;
        _pluginRegistry.PluginUnloaded -= OnPluginChanged;
        _statusService.StatusChanged -= OnStatusChanged;
        _disposed = true;
    }
}

