using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Core.Configuration;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class ProjectHomeViewModel : ObservableObject
{
    private readonly ILogger<ProjectHomeViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICrawlEngine _crawlEngine;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private int _maxDepth = 5;

    [ObservableProperty]
    private int _maxUrls = 1000;

    [ObservableProperty]
    private double _crawlDelay = 1.0;

    [ObservableProperty]
    private bool _respectRobotsTxt = true;

    [ObservableProperty]
    private bool _useSitemapXml = true;

    [ObservableProperty]
    private UserAgentType _selectedUserAgentType = UserAgentType.Chrome;

    [ObservableProperty]
    private ObservableCollection<Project> _recentProjects = new();

    [ObservableProperty]
    private Project? _currentProject;

    [ObservableProperty]
    private bool _isWelcomeScreen = true;

    public ProjectHomeViewModel(
        ILogger<ProjectHomeViewModel> logger,
        INavigationService navigationService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ICrawlEngine crawlEngine)
    {
        _logger = logger;
        _navigationService = navigationService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _crawlEngine = crawlEngine;
    }

    public async Task LoadAsync()
    {
        // Check if a project is currently open
        if (_projectContext.HasOpenProject && _projectContext.CurrentProjectId.HasValue)
        {
            try
            {
                // Load the current project details
                using var scope = _serviceProvider.CreateScope();
                var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
                var project = await projectRepo.GetByIdAsync(_projectContext.CurrentProjectId.Value);

                if (project != null)
                {
                    CurrentProject = project;
                    ProjectName = project.Name;
                    BaseUrl = project.BaseUrl;

                    // Load settings
                    var settings = JsonSerializer.Deserialize<ProjectSettings>(project.SettingsJson) ?? new ProjectSettings();
                    MaxDepth = settings.MaxCrawlDepth;
                    MaxUrls = settings.MaxUrlsToCrawl;
                    CrawlDelay = settings.CrawlDelaySeconds;
                    RespectRobotsTxt = settings.RespectRobotsTxt;
                    UseSitemapXml = settings.UseSitemapXml;
                    SelectedUserAgentType = settings.UserAgentType;

                    IsWelcomeScreen = false;
                    _logger.LogInformation("Loaded project details for {ProjectName}", project.Name);
                }
                else
                {
                    // Project not found, show welcome screen
                    IsWelcomeScreen = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load project details");
                IsWelcomeScreen = true;
            }
        }
        else
        {
            // No project open, show welcome screen
            IsWelcomeScreen = true;
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        IsWelcomeScreen = false;
        CurrentProject = null;
        ProjectName = string.Empty;
        BaseUrl = string.Empty;
        MaxDepth = 5;
        MaxUrls = 1000;
        CrawlDelay = 1.0;
        RespectRobotsTxt = true;
        UseSitemapXml = true;
        SelectedUserAgentType = UserAgentType.Chrome;
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(Project project)
    {
        if (project == null)
            return;

        // For now, recent projects functionality is deferred
        // This would require a master database to track all project files
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var dialog = new VistaOpenFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db|All files (*.*)|*.*",
            Title = "Open Project",
            InitialDirectory = GetProjectsDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Switch to the selected database
                var dbProvider = _serviceProvider.GetRequiredService<ShoutingIguana.Data.ProjectDbContextProvider>();
                await dbProvider.SetProjectPathAsync(dialog.FileName);

                // Create a scoped repository to load from the new database
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
                _projectContext.OpenProject(dialog.FileName, project.Id, project.Name);

                // Update UI
                CurrentProject = project;
                ProjectName = project.Name;
                BaseUrl = project.BaseUrl;

                // Load settings
                var settings = JsonSerializer.Deserialize<ProjectSettings>(project.SettingsJson) ?? new ProjectSettings();
                MaxDepth = settings.MaxCrawlDepth;
                MaxUrls = settings.MaxUrlsToCrawl;
                CrawlDelay = settings.CrawlDelaySeconds;
                RespectRobotsTxt = settings.RespectRobotsTxt;
                UseSitemapXml = settings.UseSitemapXml;
                SelectedUserAgentType = settings.UserAgentType;

                // Update last opened time
                project.LastOpenedUtc = DateTime.UtcNow;
                await projectRepo.UpdateAsync(project);

                IsWelcomeScreen = false;
                _logger.LogInformation("Opened project: {ProjectName} from {FilePath}", project.Name, dialog.FileName);

                // Check for crash recovery
                await CheckForCrashRecoveryAsync(project.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open project from {FilePath}", dialog.FileName);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync(bool showSuccessMessage = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Project name is required", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Base URL is required", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Invalid URL format", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var settings = new ProjectSettings
            {
                BaseUrl = BaseUrl,
                MaxCrawlDepth = MaxDepth,
                MaxUrlsToCrawl = MaxUrls,
                CrawlDelaySeconds = CrawlDelay,
                RespectRobotsTxt = RespectRobotsTxt,
                UseSitemapXml = UseSitemapXml,
                UserAgentType = SelectedUserAgentType
            };

            // Perform save operation on background thread
            await Task.Run(async () =>
            {
                if (CurrentProject == null)
                {
                    // Create new project with its own database file
                    var projectsDir = GetProjectsDirectory();
                    var sanitizedName = string.Join("_", ProjectName.Split(Path.GetInvalidFileNameChars()));
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var projectFileName = $"{sanitizedName}_{timestamp}.db";
                    var projectPath = Path.Combine(projectsDir, projectFileName);

                    // Switch to new database
                    var dbProvider = _serviceProvider.GetRequiredService<ShoutingIguana.Data.ProjectDbContextProvider>();
                    await dbProvider.SetProjectPathAsync(projectPath);

                    // Create project in new database
                    using var scope = _serviceProvider.CreateScope();
                    var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
                    
                    var project = new Project
                    {
                        Name = ProjectName,
                        BaseUrl = BaseUrl,
                        CreatedUtc = DateTime.UtcNow,
                        LastOpenedUtc = DateTime.UtcNow,
                        SettingsJson = JsonSerializer.Serialize(settings)
                    };

                    CurrentProject = await projectRepo.CreateAsync(project);
                    
                    // Update project context
                    _projectContext.OpenProject(projectPath, CurrentProject.Id, CurrentProject.Name);
                    
                    _logger.LogInformation("Created new project: {ProjectName} at {ProjectPath}", ProjectName, projectPath);
                }
                else
                {
                    // Update existing project
                    using var scope = _serviceProvider.CreateScope();
                    var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
                    
                    CurrentProject.Name = ProjectName;
                    CurrentProject.BaseUrl = BaseUrl;
                    CurrentProject.SettingsJson = JsonSerializer.Serialize(settings);
                    CurrentProject.LastOpenedUtc = DateTime.UtcNow;
                    await projectRepo.UpdateAsync(CurrentProject);
                    
                    // Update project context name
                    if (_projectContext.CurrentProjectPath != null)
                    {
                        _projectContext.OpenProject(_projectContext.CurrentProjectPath, CurrentProject.Id, CurrentProject.Name);
                    }
                    
                    _logger.LogInformation("Updated project: {ProjectName}", ProjectName);
                }
            });

            if (showSuccessMessage)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Settings saved successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project settings");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCrawl))]
    private async Task StartCrawlAsync()
    {
        // Always save settings before starting crawl to ensure any changes are persisted
        await SaveSettingsAsync(showSuccessMessage: false);
        
        // If save failed or was cancelled, don't proceed
        if (CurrentProject == null)
        {
            return;
        }

        // Validate that the base URL is reachable before starting the crawl
        // This runs on a background thread to avoid blocking the UI
        var validationSuccess = false;
        
        await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Testing connectivity to {BaseUrl}", BaseUrl);
                
                // Update UI on dispatcher
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Show validation status (could be bound to UI)
                    _logger.LogDebug("Validating connection...");
                });
                
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5); // Quick test
                var testUserAgent = new ProjectSettings { UserAgentType = SelectedUserAgentType }.GetUserAgentString();
                httpClient.DefaultRequestHeaders.Add("User-Agent", testUserAgent);
                
                await httpClient.GetAsync(BaseUrl);
                _logger.LogInformation("Successfully connected to {BaseUrl}", BaseUrl);
                validationSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to {BaseUrl}", BaseUrl);
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"Cannot connect to {BaseUrl}\n\nError: {ex.Message}\n\nPlease check:\n• The URL is correct\n• The domain exists\n• You have internet connectivity\n• The site is not blocking requests",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
            }
        });

        if (!validationSuccess)
        {
            return;
        }

        // Navigate to crawl dashboard with project context
        _navigationService.NavigateTo<ShoutingIguana.Views.CrawlDashboardView>();
        _logger.LogInformation("Navigating to crawl dashboard for project {ProjectId}", CurrentProject.Id);
    }
    
    private bool CanStartCrawl()
    {
        return !string.IsNullOrWhiteSpace(BaseUrl) && 
               !string.IsNullOrWhiteSpace(ProjectName) &&
               Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
    }

    private string GetProjectsDirectory()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShoutingIguana",
            "projects");

        Directory.CreateDirectory(path);
        return path;
    }

    private async Task CheckForCrashRecoveryAsync(int projectId)
    {
        try
        {
            // Check if there's an active checkpoint (indicates incomplete crawl)
            var checkpoint = await _crawlEngine.GetActiveCheckpointAsync(projectId);
            
            if (checkpoint != null)
            {
                _logger.LogWarning("Found active checkpoint from previous crawl - {UrlsCrawled} URLs crawled, {ErrorCount} errors, {QueueSize} in queue",
                    checkpoint.UrlsCrawled, checkpoint.ErrorCount, checkpoint.QueueSize);

                var elapsed = TimeSpan.FromSeconds(checkpoint.ElapsedSeconds);
                var message = $"This project has an incomplete crawl from a previous session.{Environment.NewLine}{Environment.NewLine}" +
                              $"Progress: {checkpoint.UrlsCrawled} URLs crawled{Environment.NewLine}" +
                              $"Queue: {checkpoint.QueueSize} URLs remaining{Environment.NewLine}" +
                              $"Errors: {checkpoint.ErrorCount}{Environment.NewLine}" +
                              $"Time: {elapsed:hh\\:mm\\:ss}{Environment.NewLine}{Environment.NewLine}" +
                              $"Would you like to resume the crawl?";

                var result = await Application.Current.Dispatcher.InvokeAsync(() => 
                    MessageBox.Show(
                        message,
                        "Resume Previous Crawl",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question));

                if (result == MessageBoxResult.Yes)
                {
                    _logger.LogInformation("User chose to resume previous crawl from checkpoint");
                    
                    // Reset any InProgress items back to Queued so they can be picked up
                    using var scope = _serviceProvider.CreateScope();
                    var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                    await queueRepo.ResetInProgressItemsAsync(projectId);
                    
                    // Navigate to crawl dashboard and start
                    _navigationService.NavigateTo<Views.CrawlDashboardView>();
                    await _crawlEngine.StartCrawlAsync(projectId);
                }
                else
                {
                    _logger.LogInformation("User chose not to resume, deactivating checkpoint");
                    
                    // Deactivate the checkpoint
                    using var scope = _serviceProvider.CreateScope();
                    var checkpointRepo = scope.ServiceProvider.GetRequiredService<ICrawlCheckpointRepository>();
                    await checkpointRepo.DeactivateCheckpointsAsync(projectId);
                    
                    // Reset InProgress queue items to Queued state
                    var queueRepo = scope.ServiceProvider.GetRequiredService<ICrawlQueueRepository>();
                    await queueRepo.ResetInProgressItemsAsync(projectId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for crash recovery");
        }
    }
}

