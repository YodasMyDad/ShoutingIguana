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
    private readonly ICsvExportService _csvExportService;
    private readonly IExcelExportService _excelExportService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPluginConfigurationService _pluginConfig;
    private bool _disposed;
    private FindingTabViewModel? _previousTab;

    [ObservableProperty]
    private ObservableCollection<FindingTabViewModel> _tabs = new();

    [ObservableProperty]
    private FindingTabViewModel? _selectedTab;

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

    public FindingsViewModel(
        ILogger<FindingsViewModel> logger,
        ICsvExportService csvExportService,
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider,
        IPluginConfigurationService pluginConfig)
    {
        _logger = logger;
        _csvExportService = csvExportService;
        _excelExportService = excelExportService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
        _pluginConfig = pluginConfig;
        
        // Subscribe to plugin state changes to refresh tabs
        _pluginConfig.PluginStateChanged += OnPluginStateChanged;
    }

    partial void OnSelectedTabChanged(FindingTabViewModel? value)
    {
        // Unsubscribe from previous tab
        if (_previousTab != null)
        {
            _previousTab.PropertyChanged -= OnTabPropertyChanged;
        }

        // Subscribe to new tab
        if (value != null)
        {
            value.PropertyChanged += OnTabPropertyChanged;

            // If no selection on the new tab, select the first item to show details
            if (value.SelectedFinding == null && value.FilteredFindings.Count > 0)
            {
                value.SelectedFinding = value.FilteredFindings[0];
            }

            UpdateDetailsPanel();
        }

        _previousTab = value;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update when the selected finding changes
        // The FindingTabViewModel will have already updated its detail properties in its partial method
        if (e.PropertyName == nameof(FindingTabViewModel.SelectedFinding)
            || e.PropertyName == nameof(FindingTabViewModel.SelectedFindingDetails)
            || e.PropertyName == nameof(FindingTabViewModel.HasTechnicalMetadata)
            || e.PropertyName == nameof(FindingTabViewModel.TechnicalMetadataJson))
        {
            _logger.LogDebug("SelectedFinding changed, updating details panel");
            // Use Dispatcher to ensure UI updates happen on the UI thread
            Application.Current.Dispatcher.InvokeAsync(() => UpdateDetailsPanel());
        }
    }

    private void UpdateDetailsPanel()
    {
        if (SelectedTab?.SelectedFinding == null)
        {
            _logger.LogDebug("No selected finding, clearing details");
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
            return;
        }

        var finding = SelectedTab.SelectedFinding;
        _logger.LogDebug("Updating details panel for finding: {FindingId}, URL: {Url}", 
            finding.Id, finding.Url.Address);
        
        // Directly update backing fields and manually raise PropertyChanged events
        // This bypasses the equality check in the generated property setters to force UI updates
#pragma warning disable MVVMTK0034 // Direct field reference instead of property
        _selectedFindingDetails = SelectedTab.SelectedFindingDetails;
        _hasTechnicalMetadata = SelectedTab.HasTechnicalMetadata;
        _technicalMetadataJson = SelectedTab.TechnicalMetadataJson;
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
            var pluginRegistry = scope.ServiceProvider.GetRequiredService<IPluginRegistry>();
            
            var projectId = _projectContext.CurrentProjectId!.Value;
            var allFindings = await findingRepository.GetByProjectIdAsync(projectId);
            
            // Get task metadata from plugin registry
            var registeredTasks = pluginRegistry.RegisteredTasks;
            var taskMetadata = registeredTasks.ToDictionary(t => t.Key, t => (t.DisplayName, t.Description));
            
            // Get enabled tasks to filter findings
            var enabledTasks = pluginRegistry.EnabledTasks;
            var enabledTaskKeys = new HashSet<string>(enabledTasks.Select(t => t.Key));
            
            // Group findings by task key
            var grouped = allFindings.GroupBy(f => f.TaskKey);

            var tabs = new List<FindingTabViewModel>();
            
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                var taskKey = group.Key;
                
                // Only show tabs for enabled plugins
                if (!enabledTaskKeys.Contains(taskKey))
                {
                    _logger.LogDebug("Skipping tab for disabled plugin task: {TaskKey}", taskKey);
                    continue;
                }
                
                var displayName = taskMetadata.TryGetValue(taskKey, out var metadata) 
                    ? metadata.DisplayName 
                    : taskKey;
                var description = taskMetadata.TryGetValue(taskKey, out var meta) 
                    ? meta.Description 
                    : string.Empty;
                
                var tab = new FindingTabViewModel
                {
                    TaskKey = taskKey,
                    DisplayName = displayName,
                    Description = description
                };
                tab.LoadFindings(group);
                tabs.Add(tab);
            }

            // Clear old tabs to help GC
            if (Tabs.Count > 0)
            {
                foreach (var oldTab in Tabs)
                {
                    oldTab.Findings.Clear();
                    oldTab.FilteredFindings.Clear();
                }
            }

            Tabs = new ObservableCollection<FindingTabViewModel>(tabs);
            
            // Select first tab
            if (Tabs.Count > 0)
            {
                SelectedTab = Tabs[0];
            }
            
            _logger.LogInformation("Loaded findings: {TabCount} tabs, {FindingCount} total findings", 
                Tabs.Count, allFindings.Count);
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
            // Show export options dialog
            string exportFormat = "Excel";
            bool includeTechnicalMetadata = false;
            bool includeErrors = true;
            bool includeWarnings = true;
            bool includeInfo = true;
            bool? optionsResult = null;
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var optionsDialog = new Views.ExportOptionsDialog
                {
                    Owner = Application.Current.MainWindow
                };
                
                optionsResult = optionsDialog.ShowDialog();
                if (optionsResult == true)
                {
                    exportFormat = optionsDialog.ExportFormat;
                    includeTechnicalMetadata = optionsDialog.IncludeTechnicalMetadata;
                    includeErrors = optionsDialog.IncludeErrors;
                    includeWarnings = optionsDialog.IncludeWarnings;
                    includeInfo = optionsDialog.IncludeInfo;
                }
            });
            
            // User cancelled export options
            if (optionsResult != true)
            {
                return;
            }
            
            // Validate at least one severity is selected
            if (!includeErrors && !includeWarnings && !includeInfo)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Please select at least one severity level to export.", "No Severity Selected", 
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }
            
            // Determine file filter and extension based on format
            string filter, defaultExt, fileName;
            if (exportFormat == "CSV")
            {
                filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                defaultExt = "csv";
                fileName = $"shouting-iguana-findings-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            }
            else
            {
                filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                defaultExt = "xlsx";
                fileName = $"shouting-iguana-findings-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
            }
            
            var dialog = new VistaSaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = fileName
            };

            if (dialog.ShowDialog() == true)
            {
                var projectId = _projectContext.CurrentProjectId!.Value;
                
                if (exportFormat == "CSV")
                {
                    await _csvExportService.ExportFindingsAsync(projectId, dialog.FileName, includeTechnicalMetadata, includeErrors, includeWarnings, includeInfo);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Exported to {dialog.FileName}", "Export Successful", 
                            MessageBoxButton.OK, MessageBoxImage.Information));
                    _logger.LogInformation("Exported findings to CSV: {FilePath} (Technical: {Tech}, Errors: {Err}, Warnings: {Warn}, Info: {Info})", 
                        dialog.FileName, includeTechnicalMetadata, includeErrors, includeWarnings, includeInfo);
                }
                else
                {
                    var success = await _excelExportService.ExportFindingsAsync(projectId, dialog.FileName, includeTechnicalMetadata, includeErrors, includeWarnings, includeInfo);
                    
                    if (success)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            MessageBox.Show($"Exported to {dialog.FileName}", "Export Successful", 
                                MessageBoxButton.OK, MessageBoxImage.Information));
                        _logger.LogInformation("Exported findings to Excel: {FilePath} (Technical: {Tech}, Errors: {Err}, Warnings: {Warn}, Info: {Info})", 
                            dialog.FileName, includeTechnicalMetadata, includeErrors, includeWarnings, includeInfo);
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            MessageBox.Show("Export failed. Check logs for details.", "Export Failed", 
                                MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export findings");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
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
            if (SelectedTab?.SelectedFinding != null)
            {
                var finding = SelectedTab.SelectedFinding;
                var textToCopy = $"{finding.Url}\t{finding.Message}\t{finding.Severity}";
                Clipboard.SetText(textToCopy);
                _logger.LogDebug("Copied finding to clipboard");
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
            if (IsTechnicalModeEnabled)
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
    /// Recursively extracts text from a FindingDetail and its children.
    /// </summary>
    private void ExtractTextFromDetail(FindingDetail detail, List<string> lines, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        lines.Add(indent + detail.Text);

        if (detail.Children != null)
        {
            foreach (var child in detail.Children)
            {
                ExtractTextFromDetail(child, lines, indentLevel + 1);
            }
        }
    }

    private async void OnPluginStateChanged(object? sender, PluginStateChangedEventArgs e)
    {
        try
        {
            // Reload findings to show/hide tabs based on plugin enabled state
            _logger.LogInformation("Plugin state changed for {PluginId}, reloading findings view", e.PluginId);
            await LoadFindingsAsync();
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
                tab.Findings.Clear();
                tab.FilteredFindings.Clear();
            }
            Tabs.Clear();
        }

        _disposed = true;
    }
}
