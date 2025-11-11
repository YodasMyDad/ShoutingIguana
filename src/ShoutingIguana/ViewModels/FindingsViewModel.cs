using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.Core.Services;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class FindingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<FindingsViewModel> _logger;
    private readonly IExcelExportService _excelExportService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPluginConfigurationService _pluginConfig;
    private bool _disposed;
    private FindingTabViewModel? _previousTab;

    [ObservableProperty]
    private ObservableCollection<object> _tabs = new();

    [ObservableProperty]
    private object? _selectedTab;

    [ObservableProperty]
    private FindingDetails? _selectedFindingDetails;
    
    [ObservableProperty]
    private bool _hasTechnicalMetadata;
    
    [ObservableProperty]
    private string _technicalMetadataJson = string.Empty;

    [ObservableProperty]
    private bool _hasStructuredDetails;

    [ObservableProperty]
    private bool _isTechnicalModeEnabled;

    public bool IsFindingTabSelected => SelectedTab is FindingTabViewModel;
    
    public string DetailsHeaderText => SelectedTab is OverviewTabViewModel ? "URL Details" : "Finding Details";
    
    /// <summary>
    /// Show details panel for overview and finding tabs.
    /// Overview shows URL properties, finding tabs show structured details/technical metadata.
    /// </summary>
    public bool ShowDetailsPanel => SelectedTab is OverviewTabViewModel or FindingTabViewModel;

    public FindingsViewModel(
        ILogger<FindingsViewModel> logger,
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider,
        IPluginConfigurationService pluginConfig)
    {
        _logger = logger;
        _excelExportService = excelExportService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        _pluginConfig = pluginConfig;
        
        // Subscribe to plugin state changes to refresh tabs
        _pluginConfig.PluginStateChanged += OnPluginStateChanged;
    }

    partial void OnSelectedTabChanged(object? value)
    {
        // Unsubscribe from previous tab
        if (_previousTab != null)
        {
            _previousTab.PropertyChanged -= OnTabPropertyChanged;
        }

        // Subscribe to new tab if it's a FindingTabViewModel
        if (value is FindingTabViewModel findingTab)
        {
            findingTab.PropertyChanged += OnTabPropertyChanged;

            // Lazy load data if not already loaded - MUST run on background thread to not block UI
            _ = Task.Run(async () => await LoadTabDataAsync(findingTab));

            _previousTab = findingTab;
            UpdateDetailsPanel();
        }
        else if (value is OverviewTabViewModel overviewTab)
        {
            // For overview tab, select first URL if none selected
            if (overviewTab.SelectedUrlModel == null && overviewTab.FilteredUrls.Count > 0)
            {
                overviewTab.SelectedUrlModel = overviewTab.FilteredUrls[0];
            }
            
            _previousTab = null;
            UpdateDetailsPanel();
        }
        else
        {
            _previousTab = null;
        }
        
        // Notify that computed properties changed
        OnPropertyChanged(nameof(IsFindingTabSelected));
        OnPropertyChanged(nameof(DetailsHeaderText));
        OnPropertyChanged(nameof(ShowDetailsPanel));
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update when the selected finding changes
        // The FindingTabViewModel will have already updated its detail properties in its partial method
        if (e.PropertyName == nameof(FindingTabViewModel.SelectedFinding)
            || e.PropertyName == nameof(FindingTabViewModel.SelectedFindingDetails)
            || e.PropertyName == nameof(FindingTabViewModel.HasTechnicalMetadata)
            || e.PropertyName == nameof(FindingTabViewModel.TechnicalMetadataJson)
            || e.PropertyName == nameof(FindingTabViewModel.SelectedReportRow))
        {
            _logger.LogDebug("SelectedFinding changed, updating details panel");
            // Use Dispatcher to ensure UI updates happen on the UI thread
            Application.Current.Dispatcher.InvokeAsync(() => UpdateDetailsPanel());
        }
    }

    private void UpdateDetailsPanel()
    {
        // Handle FindingTabViewModel
        if (SelectedTab is FindingTabViewModel findingTab)
        {
            if (findingTab.HasDynamicSchema)
            {
                UpdateDynamicDetails(findingTab);
                return;
            }

            if (findingTab.SelectedFinding == null)
            {
                _logger.LogDebug("No selected finding, clearing details");
                ClearDetailsPanel();
                return;
            }

            var finding = findingTab.SelectedFinding;
            _logger.LogDebug("Updating details panel for finding: {FindingId}, URL: {Url}", 
                finding.Id, finding.Url.Address);
            
            ApplyDetailsFromTab(findingTab);
        }
        // Handle OverviewTabViewModel - no details panel update needed, it manages its own properties
        else if (SelectedTab is OverviewTabViewModel)
        {
            // Overview tab manages its own UrlProperties, no need to update details panel here
            _logger.LogDebug("Overview tab selected, details managed by OverviewTabViewModel");
            ClearDetailsPanel();
        }
    }

    private void UpdateDynamicDetails(FindingTabViewModel findingTab)
    {
        if (findingTab.SelectedReportRow == null)
        {
            _logger.LogDebug("No selected report row, clearing details");
            ClearDetailsPanel();
            return;
        }

        _logger.LogDebug("Updating details panel for dynamic row: {TaskKey}", findingTab.TaskKey);
        ApplyDetailsFromTab(findingTab);
    }

    private void ApplyDetailsFromTab(FindingTabViewModel findingTab)
    {
        // Directly update backing fields and manually raise PropertyChanged events
        // This bypasses the equality check in the generated property setters to force UI updates
#pragma warning disable MVVMTK0034 // Direct field reference instead of property
        _selectedFindingDetails = findingTab.SelectedFindingDetails;
        _hasTechnicalMetadata = findingTab.HasTechnicalMetadata;
        _technicalMetadataJson = findingTab.TechnicalMetadataJson;
        _hasStructuredDetails = _selectedFindingDetails?.Items.Count > 0;
#pragma warning restore MVVMTK0034
        
        // Manually raise property changed events
        OnPropertyChanged(nameof(SelectedFindingDetails));
        OnPropertyChanged(nameof(HasTechnicalMetadata));
        OnPropertyChanged(nameof(TechnicalMetadataJson));
        OnPropertyChanged(nameof(HasStructuredDetails));
        
        _logger.LogDebug("Details updated - HasDetails: {HasDetails}, HasTechMetadata: {HasTechMetadata}", 
            SelectedFindingDetails != null, HasTechnicalMetadata);
    }

    private void ClearDetailsPanel()
    {
#pragma warning disable MVVMTK0034 // Direct field reference instead of property
        _selectedFindingDetails = null;
        _hasTechnicalMetadata = false;
        _technicalMetadataJson = string.Empty;
        _hasStructuredDetails = false;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedFindingDetails));
        OnPropertyChanged(nameof(HasTechnicalMetadata));
        OnPropertyChanged(nameof(TechnicalMetadataJson));
        OnPropertyChanged(nameof(HasStructuredDetails));
    }

    private async Task LoadTabDataAsync(FindingTabViewModel findingTab)
    {
        try
        {
            // Yield immediately to let UI update with IsLoading state
            await Task.Yield();
            
            await findingTab.EnsureDataLoadedAsync();
            
            // After loading, select first item on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (findingTab.SelectedFinding == null && findingTab.FilteredFindings.Count > 0)
                {
                    findingTab.SelectedFinding = findingTab.FilteredFindings[0];
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load findings for tab: {DisplayName}", findingTab.DisplayName);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show($"Failed to load findings for {findingTab.DisplayName}: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    public async Task LoadFindingsAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            _logger.LogWarning("Cannot load findings: no project is open");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var findingRepository = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
            var projectRepository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var pluginRegistry = scope.ServiceProvider.GetRequiredService<IPluginRegistry>();
            
            var projectId = _projectContext.CurrentProjectId!.Value;
            
            // Get project to access BaseUrl
            var project = await projectRepository.GetByIdAsync(projectId);
            var baseUrl = project?.BaseUrl;
            
            // Load all URLs for Overview tab only (immediate)
            var allUrls = (await urlRepository.GetByProjectIdAsync(projectId)).ToList();
            
            // Get task metadata from plugin registry
            var registeredTasks = pluginRegistry.RegisteredTasks;
            var taskMetadata = registeredTasks.ToDictionary(t => t.Key, t => (t.DisplayName, t.Description));
            
            // Get enabled tasks to filter tabs
            var enabledTasks = pluginRegistry.EnabledTasks;
            var enabledTaskKeys = new HashSet<string>(enabledTasks.Select(t => t.Key));

            var tabs = new List<object>();
            
            // Create Overview tab first and load it immediately
            var overviewTab = new OverviewTabViewModel();
            await overviewTab.LoadUrlsAsync(allUrls, baseUrl);
            tabs.Add(overviewTab);
            
            // Sync plugin schemas to database before loading tabs
            await pluginRegistry.SyncSchemasToDatabase();
            
            // Check which tasks have custom schemas
            var schemaRepository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IReportSchemaRepository>();
            var allSchemas = await schemaRepository.GetAllAsync();
            var schemasDict = allSchemas.ToDictionary(s => s.TaskKey, s => s);
            
            // Create plugin tabs with lazy loading (no data loaded yet)
            foreach (var taskInfo in registeredTasks.Where(t => enabledTaskKeys.Contains(t.Key)).OrderBy(t => t.Key))
            {
                var taskKey = taskInfo.Key;
                var displayName = taskInfo.DisplayName;
                var description = taskInfo.Description;
                
                var tab = new FindingTabViewModel
                {
                    TaskKey = taskKey,
                    DisplayName = displayName,
                    Description = description
                };
                
                // All plugins now use report schemas - load report data
                if (schemasDict.TryGetValue(taskKey, out var schema))
                {
                    // Load report rows with custom columns
                    tab.SetDynamicLazyLoadFunction(projectId, async () =>
                    {
                        _logger.LogDebug("Lazy loading report data for task: {TaskKey}", taskKey);
                        // Create scope inside lambda to ensure proper disposal
                        using var lazyScope = _serviceProvider.CreateScope();
                        var schemaRepo = lazyScope.ServiceProvider.GetRequiredService<Core.Repositories.IReportSchemaRepository>();
                        var reportDataRepo = lazyScope.ServiceProvider.GetRequiredService<Core.Repositories.IReportDataRepository>();
                        
                        // Load schema and data
                        var taskSchema = await schemaRepo.GetByTaskKeyAsync(taskKey);
                        if (taskSchema != null)
                        {
                            await tab.LoadDynamicReportAsync(taskSchema, reportDataRepo, projectId);
                        }
                    });
                }
                else
                {
                    // No schema registered - plugin may not generate data
                    _logger.LogWarning("Plugin {TaskKey} has no registered schema", taskKey);
                }
                
                tabs.Add(tab);
            }

            // Clear old tabs to help GC
            if (Tabs.Count > 0)
            {
                foreach (var oldTab in Tabs)
                {
                    if (oldTab is FindingTabViewModel findingTab)
                    {
                        findingTab.Findings.Clear();
                        findingTab.FilteredFindings.Clear();
                    }
                    else if (oldTab is OverviewTabViewModel oldOverviewTab)
                    {
                        oldOverviewTab.Urls.Clear();
                        oldOverviewTab.FilteredUrls.Clear();
                        oldOverviewTab.UrlProperties.Clear();
                    }
                }
            }

            Tabs = new ObservableCollection<object>(tabs);
            
            // Select first tab (Overview)
            if (Tabs.Count > 0)
            {
                SelectedTab = Tabs[0];
            }
            
            _logger.LogInformation("Loaded findings view: {TabCount} tabs ({UrlCount} URLs loaded, plugin tabs lazy-loaded)", 
                Tabs.Count, allUrls.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load findings");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to load findings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return;
        }

        try
        {
            // Context-aware export: Different behavior for Overview vs Plugin tabs
            if (SelectedTab is OverviewTabViewModel)
            {
                // Overview export: Direct Excel export with all URL data (no dialog)
                await ExportOverviewAsync();
            }
            else
            {
                // Plugin tab export: Show export options dialog (existing behavior)
                await ExportFindingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private async Task ExportOverviewAsync()
    {
        var projectId = _projectContext.CurrentProjectId!.Value;
        var fileName = $"shouting-iguana-urls-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
        
        var dialog = new VistaSaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = "xlsx",
            FileName = fileName
        };

        if (dialog.ShowDialog() == true)
        {
            var success = await _excelExportService.ExportUrlsAsync(projectId, dialog.FileName);
            
            if (success)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Exported to {dialog.FileName}", "Export Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information));
                _logger.LogInformation("Exported URLs to Excel: {FilePath}", dialog.FileName);
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Export failed. Check logs for details.", "Export Failed", 
                        MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }

    private async Task ExportFindingsAsync()
    {
        // Show export options dialog with all the export logic now handled inside
        bool? dialogResult = null;
        
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var viewModelLogger = loggerFactory.CreateLogger<ExportOptionsViewModel>();
            var pluginRegistry = _serviceProvider.GetRequiredService<IPluginRegistry>();
            
            var optionsDialog = new Views.ExportOptionsDialog(
                _excelExportService,
                _projectContext,
                pluginRegistry,
                _serviceProvider,
                viewModelLogger)
            {
                Owner = Application.Current.MainWindow
            };
            
            dialogResult = optionsDialog.ShowDialog();
        });
        
        // Dialog will handle all export logic and user feedback
        // We just check if it succeeded
        if (dialogResult == true)
        {
            _logger.LogInformation("Export completed successfully");
        }
        else
        {
            _logger.LogInformation("Export was cancelled or failed");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadFindingsAsync();
    }

    [RelayCommand]
    private void CopySelected()
    {
        try
        {
            if (SelectedTab is FindingTabViewModel findingTab && findingTab.SelectedFinding != null)
            {
                var finding = findingTab.SelectedFinding;
                var textToCopy = $"{finding.Url.Address}\t{finding.Message}\t{finding.Severity}";
                Clipboard.SetText(textToCopy);
                _logger.LogDebug("Copied finding to clipboard");
            }
            else if (SelectedTab is OverviewTabViewModel overviewTab && overviewTab.SelectedUrlModel?.Url != null)
            {
                Clipboard.SetText(overviewTab.SelectedUrlModel.Url.Address);
                _logger.LogDebug("Copied URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy to clipboard");
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        try
        {
            // In WPF DataGrid, SelectAll is typically handled by the view
            // This is a placeholder that the view can override
            _logger.LogDebug("Select all requested in findings view");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select all");
        }
    }

    [RelayCommand]
    private void CopyDetails()
    {
        try
        {
            // Handle Overview tab - copy URL properties
            if (SelectedTab is OverviewTabViewModel overviewTabCopy)
            {
                if (overviewTabCopy.UrlProperties.Count > 0)
                {
                    var lines = new List<string>();
                    string? currentCategory = null;
                    
                    foreach (var prop in overviewTabCopy.UrlProperties)
                    {
                        if (prop.Category != currentCategory)
                        {
                            if (currentCategory != null)
                                lines.Add(""); // Empty line between categories
                            lines.Add($"=== {prop.Category} ===");
                            currentCategory = prop.Category;
                        }
                        lines.Add($"{prop.Key}: {prop.Value}");
                    }
                    
                    var text = string.Join(Environment.NewLine, lines);
                    if (!string.IsNullOrEmpty(text))
                    {
                        Clipboard.SetText(text);
                        _logger.LogDebug("Copied URL properties to clipboard");
                    }
                }
            }
            // Handle Finding tab
            else if (IsTechnicalModeEnabled)
            {
                // Copy technical metadata JSON
                if (!string.IsNullOrEmpty(TechnicalMetadataJson))
                {
                    Clipboard.SetText(TechnicalMetadataJson);
                    _logger.LogDebug("Copied technical metadata JSON to clipboard");
                }
            }
            else
            {
                // Copy formatted text from FindingDetails
                if (SelectedFindingDetails != null)
                {
                    var text = ExtractTextFromFindingDetails(SelectedFindingDetails);
                    if (!string.IsNullOrEmpty(text))
                    {
                        Clipboard.SetText(text);
                        _logger.LogDebug("Copied finding details text to clipboard");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy details to clipboard");
        }
    }

    /// <summary>
    /// Extracts plain text from FindingDetails hierarchy for copying.
    /// </summary>
    private string ExtractTextFromFindingDetails(FindingDetails details)
    {
        if (details.Items.Count == 0)
            return string.Empty;

        var lines = new List<string>();
        foreach (var item in details.Items)
        {
            ExtractTextFromDetail(item, lines, 0);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Extracts text from detail items (now flat strings, not hierarchical).
    /// </summary>
    private void ExtractTextFromDetail(string detail, List<string> lines, int indentLevel)
    {
        lines.Add(detail);
    }

    private async void OnPluginStateChanged(object? sender, PluginStateChangedEventArgs e)
    {
        try
        {
            // Reload findings to show/hide tabs based on plugin enabled state
            _logger.LogInformation("Plugin state changed for {PluginId}, reloading findings view", e.PluginId);
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadFindingsAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading findings after plugin state change");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Unsubscribe from plugin state changes
        _pluginConfig.PluginStateChanged -= OnPluginStateChanged;

        // Unsubscribe from current tab
        if (_previousTab != null)
        {
            _previousTab.PropertyChanged -= OnTabPropertyChanged;
        }

        // Clear all tabs to help GC
        if (Tabs.Count > 0)
        {
            foreach (var tab in Tabs)
            {
                if (tab is FindingTabViewModel findingTab)
                {
                    findingTab.Findings.Clear();
                    findingTab.FilteredFindings.Clear();
                }
                else if (tab is OverviewTabViewModel overviewTabDispose)
                {
                    overviewTabDispose.Urls.Clear();
                    overviewTabDispose.FilteredUrls.Clear();
                    overviewTabDispose.UrlProperties.Clear();
                }
            }
            Tabs.Clear();
        }

        _disposed = true;
    }
}
