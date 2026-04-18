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
using ShoutingIguana.Services.Models;
using ShoutingIguana.Views;

namespace ShoutingIguana.ViewModels;

public partial class MainViewModel(
    ILogger<MainViewModel> logger,
    INavigationService navigationService,
    IProjectContext projectContext,
    IServiceProvider serviceProvider,
    IPlaywrightService playwrightService,
    IToastService toastService,
    ICrawlEngine crawlEngine,
    IPluginRegistry pluginRegistry,
    IStatusService statusService,
    IAppSettingsService appSettingsService) : ObservableObject, IDisposable
{
    private bool _disposed;
    private bool _initialized;

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
    private bool _isCrawling;

    [ObservableProperty]
    private ObservableCollection<Core.Configuration.RecentProject> _recentProjects = new();

    /// <summary>
    /// Gets the collection of active toast notifications.
    /// </summary>
    public ObservableCollection<ToastViewModel> Toasts => toastService.Toasts;

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        navigationService.NavigationRequested += OnNavigationRequested;
        projectContext.ProjectChanged += OnProjectChanged;
        playwrightService.StatusChanged += OnBrowserStatusChanged;
        crawlEngine.ProgressUpdated += OnCrawlProgressUpdated;
        pluginRegistry.PluginLoaded += OnPluginChanged;
        pluginRegistry.PluginUnloaded += OnPluginChanged;
        statusService.StatusChanged += OnStatusChanged;

        UpdateBrowserStatus(playwrightService.Status);
        HasOpenProject = projectContext.HasOpenProject;
        UpdatePauseResumeState();
        UpdateCrawlState();
        UpdatePluginCount();
        LoadRecentProjects();

        navigationService.NavigateTo<ProjectHomeView>();
        StatusMessage = "Ready";
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        HasOpenProject = projectContext.HasOpenProject;
        ProjectName = projectContext.HasOpenProject 
            ? projectContext.CurrentProjectName ?? "Unknown Project"
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
                var recentProjects = appSettingsService.GetRecentProjects();
                
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
                        appSettingsService.RemoveRecentProject(project.FilePath);
                        removedAny = true;
                        logger.LogDebug("Removed non-existent project from recent list: {FilePath}", project.FilePath);
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
                    await appSettingsService.SaveAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading recent projects");
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
                logger.LogDebug("Disposed previous view's DataContext of type {Type}", typeName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing previous view's DataContext");
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
            UpdateCrawlState();
        });
    }

    private void UpdatePauseResumeState()
    {
        if (crawlEngine.IsPaused)
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

    private void UpdateCrawlState()
    {
        IsCrawling = crawlEngine.IsCrawling;
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
        PluginCount = pluginRegistry.LoadedPlugins.Count();
    }

    private void OnStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        // Check if a project is currently open
        if (projectContext.HasOpenProject)
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
                appSettingsService.RemoveRecentProject(recentProject.FilePath);
                await appSettingsService.SaveAsync();
                LoadRecentProjects();
                return;
            }

            // Check if a project is currently open and close it
            if (projectContext.HasOpenProject)
            {
                logger.LogInformation("Closing current project to open recent project");
                await CloseProjectAsync();
            }

            // Switch to the selected database
            var dbProvider = serviceProvider.GetRequiredService<ProjectDbContextProvider>();
            await dbProvider.SetProjectPathAsync(recentProject.FilePath);

            // Load project from database
            using var scope = serviceProvider.CreateScope();
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
            projectContext.OpenProject(recentProject.FilePath, project.Id, project.Name);

            // Update last opened time
            project.LastOpenedUtc = DateTime.UtcNow;
            await projectRepo.UpdateAsync(project);

            // Update recent projects list (this will move it to the top)
            appSettingsService.AddRecentProject(project.Name, recentProject.FilePath);
            await appSettingsService.SaveAsync();

            // Navigate to project home
            await NavigateToProjectHomeAsync();

            logger.LogInformation("Opened recent project: {ProjectName} from {FilePath}", project.Name, recentProject.FilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open recent project from {FilePath}", recentProject.FilePath);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task NavigateToProjectHomeAsync()
    {
        navigationService.NavigateTo<ProjectHomeView>();
        StatusMessage = "Project Home";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NavigateToCrawlDashboardAsync()
    {
        navigationService.NavigateTo<CrawlDashboardView>();
        StatusMessage = "Crawl Dashboard";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NavigateToFindingsAsync()
    {
        navigationService.NavigateTo<FindingsView>();
        StatusMessage = "Findings";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        if (!projectContext.HasOpenProject)
            return;

        try
        {
            // Stop crawl if running before closing project
            if (crawlEngine.IsCrawling)
            {
                logger.LogInformation("Stopping active crawl before closing project");
                await crawlEngine.StopCrawlAsync();
                
                // Give crawl time to stop gracefully
                await Task.Delay(500);
            }
            
            var projectId = projectContext.CurrentProjectId;
            
            // Cleanup plugin data for this project
            if (projectId.HasValue)
            {
                var tasks = pluginRegistry.RegisteredTasks;
                foreach (var task in tasks)
                {
                    try
                    {
                        task.CleanupProject(projectId.Value);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error cleaning up plugin task {TaskKey}", task.Key);
                    }
                }
                logger.LogDebug("Cleaned up plugin data for project {ProjectId}", projectId.Value);
            }
            
            var dbProvider = serviceProvider.GetRequiredService<ProjectDbContextProvider>();
            dbProvider.CloseProject();
            projectContext.CloseProject();
            
            await NavigateToProjectHomeAsync();
            logger.LogInformation("Closed project");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to close project");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to close project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private void Exit()
    {
        logger.LogInformation("Exiting application");
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
            var aboutDialog = new AboutDialog(serviceProvider)
            {
                Owner = Application.Current.MainWindow
            };
            aboutDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show About dialog");
            MessageBox.Show(
                "Shouting Iguana\nVersion 0.1.0 (Stage 3 MVP)\n\nA professional web crawler and SEO analysis tool.",
                "About Shouting Iguana",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task StartCrawlAsync()
    {
        if (!projectContext.HasOpenProject)
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
        if (!projectContext.HasOpenProject)
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
                using var scope = serviceProvider.CreateScope();
                var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                await queueRepo.ClearQueueAsync(projectContext.CurrentProjectId!.Value);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Queue cleared successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                logger.LogInformation("Cleared crawl queue for project {ProjectId}", projectContext.CurrentProjectId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear queue");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to clear queue: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    [RelayCommand]
    private async Task NavigateToPluginManagementAsync()
    {
        navigationService.NavigateTo<PluginManagementView>();
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
                await playwrightService.InstallBrowsersAsync();
                StatusMessage = "Browser installation complete";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Browsers installed successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                logger.LogInformation("Browsers reinstalled successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reinstall browsers");
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
            logger.LogInformation("Opening settings dialog");
            var settingsDialog = new SettingsDialog(serviceProvider)
            {
                Owner = Application.Current.MainWindow
            };
            
            var result = settingsDialog.ShowDialog();
            if (result == true)
            {
                logger.LogInformation("Settings saved successfully");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening settings dialog");
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
            else
            {
                logger.LogDebug("Copy not supported in current view");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing copy command");
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
            else
            {
                logger.LogDebug("Select All not supported in current view");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing select all command");
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

        logger.LogInformation("Find command executed - Findings view active");
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
            if (findingsVm.SelectedTab is FindingTabViewModel findingTab)
            {
                findingTab.SelectedSeverity = null;
                findingTab.SearchText = string.Empty;
                logger.LogInformation("Cleared filters on tab: {TabName}", findingTab.DisplayName);
            }
            else if (findingsVm.SelectedTab is OverviewTabViewModel overviewTab)
            {
                overviewTab.SearchText = string.Empty;
                logger.LogInformation("Cleared filters on tab: {TabName}", overviewTab.DisplayName);
            }
            else
            {
                logger.LogWarning("No tab selected to clear filters");
            }
        }
    }

    // ===== Navigation Commands =====

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (!projectContext.HasOpenProject)
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
                logger.LogInformation("Project settings saved");
            }
            else
            {
                // Navigate to project home and save
                await NavigateToProjectHomeAsync();
                
                if (CurrentView is ProjectHomeView phView && phView.DataContext is ProjectHomeViewModel phVm)
                {
                    await phVm.SaveSettingsCommand.ExecuteAsync(null);
                    logger.LogInformation("Project settings saved");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving project");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to save project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    // ===== Pause/Resume Command =====

    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        if (!crawlEngine.IsCrawling)
        {
            logger.LogWarning("Cannot pause/resume: crawl is not running");
            return;
        }

        try
        {
            if (crawlEngine.IsPaused)
            {
                logger.LogInformation("Resuming crawl");
                await crawlEngine.ResumeCrawlAsync();
                StatusMessage = "Crawl resumed";
            }
            else
            {
                logger.LogInformation("Pausing crawl");
                await crawlEngine.PauseCrawlAsync();
                StatusMessage = "Crawl paused";
            }

            UpdatePauseResumeState();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pause/resume crawl");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to pause/resume crawl: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    // ===== Destructive Actions =====

    [RelayCommand]
    private async Task ResetProjectDataAsync()
    {
        if (!projectContext.HasOpenProject)
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
                using var scope = serviceProvider.CreateScope();
                var findingRepo = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
                var imageRepo = scope.ServiceProvider.GetRequiredService<IImageRepository>();
                var redirectRepo = scope.ServiceProvider.GetRequiredService<IRedirectRepository>();
                var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();

                var projectId = projectContext.CurrentProjectId!.Value;

                // Clear all data that has DeleteByProjectIdAsync
                await queueRepo.ClearQueueAsync(projectId);
                await findingRepo.DeleteByProjectIdAsync(projectId);
                await imageRepo.DeleteByProjectIdAsync(projectId);
                await redirectRepo.DeleteByProjectIdAsync(projectId);

                logger.LogInformation("Reset project data for project {ProjectId}", projectId);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Project data has been reset (partial - URLs and Links retained)", "Success", MessageBoxButton.OK, MessageBoxImage.Information));

                // Navigate back to project home
                await NavigateToProjectHomeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset project data");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to reset project data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    // ===== Tools Menu Commands =====

    [RelayCommand]
    private void ImportUrlList()
    {
        if (!HasOpenProject || projectContext.CurrentProjectId == null)
        {
            toastService.ShowWarning("No Project", "Please open a project first");
            return;
        }

        try
        {
            var dialog = new ListModeImportDialog(serviceProvider, projectContext.CurrentProjectId.Value);
            dialog.Owner = Application.Current.MainWindow;
            var result = dialog.ShowDialog();

            if (result == true)
            {
                logger.LogInformation("URL list imported successfully");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening list import dialog");
            toastService.ShowError("Error", "Failed to open import dialog");
        }
    }

    [RelayCommand]
    private async Task CustomExtractionAsync()
    {
        if (!HasOpenProject || projectContext.CurrentProjectId == null)
        {
            toastService.ShowWarning("No Project", "Please open a project first");
            return;
        }

        try
        {
            // Navigate to Project Home
            await NavigateToProjectHomeAsync();
            
            // Switch to Custom Extraction tab (tab index 1)
            if (CurrentView is ProjectHomeView projectHomeView && projectHomeView.DataContext is ProjectHomeViewModel vm)
            {
                vm.SelectedTabIndex = 1; // Custom Extraction tab
                logger.LogInformation("Navigated to Custom Extraction tab");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error navigating to custom extraction");
            toastService.ShowError("Error", "Failed to navigate to custom extraction");
        }
    }

    [RelayCommand]
    private void TestProxy()
    {
        // Open settings dialog on Network tab
        try
        {
            var settingsDialog = new SettingsDialog(serviceProvider);
            settingsDialog.Owner = Application.Current.MainWindow;
            settingsDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening settings dialog");
            toastService.ShowError("Error", "Failed to open settings");
        }
    }

    // ===== Help Menu Commands =====

    [RelayCommand]
    private void ToggleFullScreen()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            if (window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Normal;
                logger.LogInformation("Exited full screen");
            }
            else
            {
                window.WindowState = WindowState.Maximized;
                logger.LogInformation("Entered full screen");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        navigationService.NavigationRequested -= OnNavigationRequested;
        projectContext.ProjectChanged -= OnProjectChanged;
        playwrightService.StatusChanged -= OnBrowserStatusChanged;
        crawlEngine.ProgressUpdated -= OnCrawlProgressUpdated;
        pluginRegistry.PluginLoaded -= OnPluginChanged;
        pluginRegistry.PluginUnloaded -= OnPluginChanged;
        statusService.StatusChanged -= OnStatusChanged;
        _disposed = true;
    }
}

