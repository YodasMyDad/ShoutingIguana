using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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

public partial class ProjectHomeViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly ILogger<ProjectHomeViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICrawlEngine _crawlEngine;
    private readonly IStatusService _statusService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IToastService _toastService;
    private readonly ICustomExtractionService _customExtractionService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    private string _baseUrl = string.Empty;
    
    private readonly SemaphoreSlim _autoCreateLock = new(1, 1);
    private bool _isAutoCreatingProject = false;

    [ObservableProperty]
    private int _maxDepth = 5;

    [ObservableProperty]
    private int _maxUrls = 1000;

    [ObservableProperty]
    private double _crawlDelay = 1.5;

    [ObservableProperty]
    private int _concurrentRequests = 3;

    [ObservableProperty]
    private int _timeoutSeconds = 10;

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
    
    [ObservableProperty]
    private int _selectedTabIndex;
    
    // Custom Extraction properties
    [ObservableProperty]
    private ObservableCollection<CustomExtractionRule> _extractionRules = new();
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditExtractionRule))]
    [NotifyPropertyChangedFor(nameof(CanDeleteExtractionRule))]
    [NotifyCanExecuteChangedFor(nameof(EditExtractionRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteExtractionRuleCommand))]
    private CustomExtractionRule? _selectedExtractionRule;
    
    [ObservableProperty]
    private bool _isEditingExtractionRule;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveExtractionRule))]
    [NotifyCanExecuteChangedFor(nameof(SaveExtractionRuleCommand))]
    private string _editingExtractionName = string.Empty;
    
    [ObservableProperty]
    private int _editingExtractionSelectorType;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveExtractionRule))]
    [NotifyCanExecuteChangedFor(nameof(SaveExtractionRuleCommand))]
    private string _editingExtractionSelector = string.Empty;
    
    [ObservableProperty]
    private bool _editingExtractionIsEnabled = true;
    
    [ObservableProperty]
    private int _editingExtractionRuleId;
    
    public bool CanEditExtractionRule => SelectedExtractionRule != null;
    public bool CanDeleteExtractionRule => SelectedExtractionRule != null;
    public bool CanSaveExtractionRule => !string.IsNullOrWhiteSpace(EditingExtractionName) 
                                          && !string.IsNullOrWhiteSpace(EditingExtractionSelector);

    public ProjectHomeViewModel(
        ILogger<ProjectHomeViewModel> logger,
        INavigationService navigationService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ICrawlEngine crawlEngine,
        IStatusService statusService,
        IAppSettingsService appSettingsService,
        IToastService toastService,
        ICustomExtractionService customExtractionService)
    {
        _logger = logger;
        _navigationService = navigationService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _crawlEngine = crawlEngine;
        _statusService = statusService;
        _appSettingsService = appSettingsService;
        _toastService = toastService;
        _customExtractionService = customExtractionService;
    }
    
    // Property change handlers to auto-create project
    partial void OnProjectNameChanged(string value)
    {
        _ = TryAutoCreateProjectAsync();
    }
    
    partial void OnBaseUrlChanged(string value)
    {
        _ = TryAutoCreateProjectAsync();
    }
    
    /// <summary>
    /// Auto-creates the project database when both name and URL are valid and no project exists yet.
    /// This allows users to add extraction rules before starting the first crawl.
    /// </summary>
    private async Task TryAutoCreateProjectAsync()
    {
        // Quick check without lock - avoid unnecessary lock contention
        if (_isAutoCreatingProject || 
            CurrentProject != null || 
            string.IsNullOrWhiteSpace(ProjectName) || 
            string.IsNullOrWhiteSpace(BaseUrl) ||
            IsWelcomeScreen ||
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            return;
        }
        
        // Try to acquire lock without blocking - if another call is already creating, skip
        if (!await _autoCreateLock.WaitAsync(0))
        {
            return;
        }
        
        try
        {
            // Double-check conditions after acquiring lock
            if (_isAutoCreatingProject || 
                CurrentProject != null || 
                string.IsNullOrWhiteSpace(ProjectName) || 
                string.IsNullOrWhiteSpace(BaseUrl) ||
                IsWelcomeScreen ||
                !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            {
                return;
            }
            
            _isAutoCreatingProject = true;
            
            // Capture properties on UI thread before going to background
            var projectName = ProjectName;
            var baseUrl = BaseUrl;
            
            _logger.LogInformation("Auto-creating project database for: {ProjectName}", projectName);
            
            var settings = new ProjectSettings
            {
                BaseUrl = baseUrl,
                MaxCrawlDepth = MaxDepth,
                MaxUrlsToCrawl = MaxUrls,
                CrawlDelaySeconds = CrawlDelay,
                ConcurrentRequests = ConcurrentRequests,
                TimeoutSeconds = TimeoutSeconds,
                RespectRobotsTxt = RespectRobotsTxt,
                UseSitemapXml = UseSitemapXml,
                UserAgentType = SelectedUserAgentType
            };
            
            Project? createdProject = null;
            
            await Task.Run(async () =>
            {
                var projectsDir = GetProjectsDirectory();
                var sanitizedName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));
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
                    Name = projectName,
                    BaseUrl = baseUrl,
                    CreatedUtc = DateTime.UtcNow,
                    LastOpenedUtc = DateTime.UtcNow,
                    SettingsJson = JsonSerializer.Serialize(settings)
                };

                createdProject = await projectRepo.CreateAsync(project);
                
                // Update project context (safe to call from background thread)
                _projectContext.OpenProject(projectPath, createdProject.Id, createdProject.Name);
                
                // Add to recent projects
                _appSettingsService.AddRecentProject(createdProject.Name, projectPath);
                await _appSettingsService.SaveAsync();
                
                _logger.LogInformation("Auto-created project: {ProjectName} at {ProjectPath}", projectName, projectPath);
            });
            
            // Update CurrentProject on UI thread to ensure PropertyChanged fires on UI thread
            if (createdProject != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CurrentProject = createdProject;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-create project database");
            // Don't show error to user - this is a background operation
            // They can still manually save via Start Crawl
        }
        finally
        {
            _isAutoCreatingProject = false;
            _autoCreateLock.Release();
        }
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
                    ConcurrentRequests = settings.ConcurrentRequests;
                    TimeoutSeconds = settings.TimeoutSeconds;
                    RespectRobotsTxt = settings.RespectRobotsTxt;
                    UseSitemapXml = settings.UseSitemapXml;
                    SelectedUserAgentType = settings.UserAgentType;

                    IsWelcomeScreen = false;
                    _logger.LogInformation("Loaded project details for {ProjectName}", project.Name);
                    
                    // Load custom extraction rules
                    await LoadExtractionRulesAsync();
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
    
    private async Task LoadExtractionRulesAsync()
    {
        if (!_projectContext.CurrentProjectId.HasValue)
            return;
        
        try
        {
            var rules = await _customExtractionService.GetRulesByProjectIdAsync(_projectContext.CurrentProjectId.Value);
            
            // Ensure ObservableCollection update happens on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ExtractionRules = new ObservableCollection<CustomExtractionRule>(rules);
            });
            
            _logger.LogInformation("Loaded {Count} extraction rules", rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading extraction rules");
            _toastService.ShowError("Error", "Failed to load extraction rules");
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
        CrawlDelay = 1.5;
        ConcurrentRequests = 3;
        TimeoutSeconds = 10;
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
                ConcurrentRequests = settings.ConcurrentRequests;
                TimeoutSeconds = settings.TimeoutSeconds;
                RespectRobotsTxt = settings.RespectRobotsTxt;
                UseSitemapXml = settings.UseSitemapXml;
                SelectedUserAgentType = settings.UserAgentType;

                // Update last opened time
                project.LastOpenedUtc = DateTime.UtcNow;
                await projectRepo.UpdateAsync(project);

                IsWelcomeScreen = false;
                _logger.LogInformation("Opened project: {ProjectName} from {FilePath}", project.Name, dialog.FileName);

                // Add to recent projects
                _appSettingsService.AddRecentProject(project.Name, dialog.FileName);
                await _appSettingsService.SaveAsync();
                
                // Load custom extraction rules
                await LoadExtractionRulesAsync();

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
                ConcurrentRequests = ConcurrentRequests,
                TimeoutSeconds = TimeoutSeconds,
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
                    // (This should rarely happen now due to auto-create, but keep as fallback)
                    _statusService.UpdateStatus("Creating project database...");
                    var projectsDir = GetProjectsDirectory();
                    var sanitizedName = string.Join("_", ProjectName.Split(Path.GetInvalidFileNameChars()));
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var projectFileName = $"{sanitizedName}_{timestamp}.db";
                    var projectPath = Path.Combine(projectsDir, projectFileName);

                    // Switch to new database
                    _statusService.UpdateStatus("Initializing database schema...");
                    var dbProvider = _serviceProvider.GetRequiredService<ShoutingIguana.Data.ProjectDbContextProvider>();
                    await dbProvider.SetProjectPathAsync(projectPath);

                    // Create project in new database
                    _statusService.UpdateStatus("Saving project settings...");
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
                    
                    // Add to recent projects
                    _appSettingsService.AddRecentProject(CurrentProject.Name, projectPath);
                    await _appSettingsService.SaveAsync();
                    
                    _logger.LogInformation("Created new project: {ProjectName} at {ProjectPath}", ProjectName, projectPath);
                    _statusService.UpdateStatus("Project created successfully");
                }
                else
                {
                    // Update existing project (project was auto-created or already exists)
                    _statusService.UpdateStatus("Updating project settings...");
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
                    _statusService.UpdateStatus("Project updated successfully");
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
                _statusService.UpdateStatus($"Validating connection to {BaseUrl}...");
                
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5); // Quick test
                var testUserAgent = new ProjectSettings { UserAgentType = SelectedUserAgentType }.GetUserAgentString();
                httpClient.DefaultRequestHeaders.Add("User-Agent", testUserAgent);
                
                await httpClient.GetAsync(BaseUrl);
                _logger.LogInformation("Successfully connected to {BaseUrl}", BaseUrl);
                _statusService.UpdateStatus("Connection validated");
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
        _statusService.UpdateStatus("Starting crawl...");
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
    
    // ===== Custom Extraction Commands =====
    
    [RelayCommand]
    private void AddExtractionRule()
    {
        IsEditingExtractionRule = true;
        EditingExtractionRuleId = 0;
        EditingExtractionName = string.Empty;
        EditingExtractionSelectorType = 0; // CSS by default
        EditingExtractionSelector = string.Empty;
        EditingExtractionIsEnabled = true;
        
        _logger.LogDebug("Starting new extraction rule creation");
    }
    
    [RelayCommand(CanExecute = nameof(CanEditExtractionRule))]
    private void EditExtractionRule()
    {
        if (SelectedExtractionRule == null) return;

        IsEditingExtractionRule = true;
        EditingExtractionRuleId = SelectedExtractionRule.Id;
        EditingExtractionName = SelectedExtractionRule.Name;
        EditingExtractionSelectorType = SelectedExtractionRule.SelectorType;
        EditingExtractionSelector = SelectedExtractionRule.Selector;
        EditingExtractionIsEnabled = SelectedExtractionRule.IsEnabled;
        
        _logger.LogDebug("Editing extraction rule: {RuleName}", SelectedExtractionRule.Name);
    }
    
    [RelayCommand(CanExecute = nameof(CanDeleteExtractionRule))]
    private async Task DeleteExtractionRuleAsync()
    {
        if (SelectedExtractionRule == null) return;

        var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                $"Are you sure you want to delete the rule '{SelectedExtractionRule.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question));

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _customExtractionService.DeleteRuleAsync(SelectedExtractionRule.Id);
                ExtractionRules.Remove(SelectedExtractionRule);
                SelectedExtractionRule = null;
                
                _toastService.ShowSuccess("Deleted", "Rule deleted successfully");
                _logger.LogInformation("Deleted extraction rule");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting extraction rule");
                _toastService.ShowError("Error", "Failed to delete rule");
            }
        }
    }
    
    [RelayCommand(CanExecute = nameof(CanSaveExtractionRule))]
    private async Task SaveExtractionRuleAsync()
    {
        if (!_projectContext.CurrentProjectId.HasValue)
        {
            _toastService.ShowWarning("No Project", "Please create or open a project first");
            return;
        }
        
        try
        {
            var rule = new CustomExtractionRule
            {
                Id = EditingExtractionRuleId,
                ProjectId = _projectContext.CurrentProjectId.Value,
                Name = EditingExtractionName.Trim(),
                FieldName = GenerateFieldName(EditingExtractionName.Trim()),
                SelectorType = EditingExtractionSelectorType,
                Selector = EditingExtractionSelector.Trim(),
                IsEnabled = EditingExtractionIsEnabled
            };

            var savedRule = await _customExtractionService.SaveRuleAsync(rule);

            // Update UI
            if (EditingExtractionRuleId == 0)
            {
                // New rule
                ExtractionRules.Add(savedRule);
                _toastService.ShowSuccess("Created", "Rule created successfully");
            }
            else
            {
                // Update existing
                var existing = ExtractionRules.FirstOrDefault(r => r.Id == EditingExtractionRuleId);
                if (existing != null)
                {
                    var index = ExtractionRules.IndexOf(existing);
                    ExtractionRules[index] = savedRule;
                }
                _toastService.ShowSuccess("Updated", "Rule updated successfully");
            }

            IsEditingExtractionRule = false;
            _logger.LogInformation("Saved extraction rule: {RuleName}", savedRule.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving extraction rule");
            _toastService.ShowError("Error", "Failed to save rule");
        }
    }
    
    [RelayCommand]
    private void CancelEditExtractionRule()
    {
        IsEditingExtractionRule = false;
        _logger.LogDebug("Cancelled extraction rule editing");
    }
    
    /// <summary>
    /// Saves the IsEnabled toggle change for an extraction rule.
    /// Called when the user toggles the checkbox in the DataGrid.
    /// </summary>
    public async Task SaveExtractionRuleToggleAsync(CustomExtractionRule rule)
    {
        try
        {
            await _customExtractionService.SaveRuleAsync(rule);
            _logger.LogDebug("Toggled extraction rule '{RuleName}' to {IsEnabled}", rule.Name, rule.IsEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling extraction rule");
            _toastService.ShowError("Error", "Failed to update rule");
        }
    }
    
    /// <summary>
    /// Generates a valid field name from a rule name by converting to lowercase,
    /// replacing spaces with underscores, and removing special characters.
    /// </summary>
    private static string GenerateFieldName(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            return "unnamed_field";
            
        // Convert to lowercase and replace spaces with underscores
        var fieldName = ruleName.ToLowerInvariant().Replace(' ', '_');
        
        // Remove all characters except alphanumeric and underscores
        fieldName = System.Text.RegularExpressions.Regex.Replace(fieldName, @"[^a-z0-9_]", "");
        
        // Ensure it doesn't start with a number (add prefix if needed)
        if (fieldName.Length > 0 && char.IsDigit(fieldName[0]))
            fieldName = "field_" + fieldName;
            
        return string.IsNullOrWhiteSpace(fieldName) ? "unnamed_field" : fieldName;
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _autoCreateLock?.Dispose();
        _disposed = true;
    }
}

