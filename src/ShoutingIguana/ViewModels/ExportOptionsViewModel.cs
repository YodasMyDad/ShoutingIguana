using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Core.Services;
using ShoutingIguana.Services;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the export options dialog.
/// </summary>
public partial class ExportOptionsViewModel : ObservableObject
{
    private readonly Window _dialog;
    private readonly IExcelExportService _excelExportService;
    private readonly IProjectContext _projectContext;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExportOptionsViewModel> _logger;
    
    [ObservableProperty]
    private bool _includeErrors = true;
    
    [ObservableProperty]
    private bool _includeWarnings = true;
    
    [ObservableProperty]
    private bool _includeInfo = true;
    
    [ObservableProperty]
    private bool _isExporting;
    
    [ObservableProperty]
    private string _exportStatus = "";
    
    [ObservableProperty]
    private ObservableCollection<PluginSelectionItem> _pluginSelectionItems = new();
    
    public bool ExportSucceeded { get; private set; }
    
    public ExportOptionsViewModel(
        Window dialog, 
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        IPluginRegistry pluginRegistry,
        IServiceProvider serviceProvider,
        ILogger<ExportOptionsViewModel> logger)
    {
        _dialog = dialog;
        _excelExportService = excelExportService;
        _projectContext = projectContext;
        _pluginRegistry = pluginRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Load plugins asynchronously
        _ = LoadPluginsAsync();
    }
    
    private async Task LoadPluginsAsync()
    {
        try
        {
            var projectId = _projectContext.CurrentProjectId;
            if (!projectId.HasValue) return;
            
            // Get enabled tasks from plugin registry
            var enabledTasks = _pluginRegistry.EnabledTasks;
            
            // Get finding/report counts per plugin
            using var scope = _serviceProvider.CreateScope();
            var findingRepository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IFindingRepository>();
            var reportSchemaRepository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IReportSchemaRepository>();
            var reportDataRepository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IReportDataRepository>();
            
            var allSchemas = await reportSchemaRepository.GetAllAsync();
            var schemasDict = allSchemas.ToDictionary(s => s.TaskKey, s => s);
            
            var items = new ObservableCollection<PluginSelectionItem>();
            
            foreach (var task in enabledTasks.OrderBy(t => t.DisplayName))
            {
                int count = 0;
                
                // Check if has custom schema (use report count) or legacy (use finding count)
                if (schemasDict.ContainsKey(task.Key))
                {
                    count = await reportDataRepository.GetCountByTaskKeyAsync(projectId.Value, task.Key);
                }
                else
                {
                    count = await findingRepository.GetCountByTaskKeyAsync(projectId.Value, task.Key);
                }
                
                items.Add(new PluginSelectionItem
                {
                    TaskKey = task.Key,
                    DisplayName = task.DisplayName,
                    IsSelected = true,
                    FindingCount = count
                });
            }
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PluginSelectionItems = items;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugins for export dialog");
        }
    }
    
    [RelayCommand]
    private void SelectAllPlugins()
    {
        foreach (var item in PluginSelectionItems)
        {
            item.IsSelected = true;
        }
    }
    
    [RelayCommand]
    private void DeselectAllPlugins()
    {
        foreach (var item in PluginSelectionItems)
        {
            item.IsSelected = false;
        }
    }
    
    [RelayCommand]
    private async Task OkAsync()
    {
        try
        {
            // Validate at least one severity is selected
            if (!IncludeErrors && !IncludeWarnings && !IncludeInfo)
            {
                MessageBox.Show(
                    "Please select at least one severity level to export.", 
                    "No Severity Selected", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }
            
            // Validate at least one plugin is selected
            var selectedPlugins = PluginSelectionItems.Where(p => p.IsSelected).ToList();
            if (selectedPlugins.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one plugin to export.", 
                    "No Plugins Selected", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                return;
            }
            
            var selectedTaskKeys = selectedPlugins.Select(p => p.TaskKey).ToList();
            
            var dialog = new VistaSaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                DefaultExt = "xlsx",
                FileName = $"shouting-iguana-findings-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return; // User cancelled file selection
            }
            
            // Start exporting - update UI on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsExporting = true;
                ExportStatus = "Preparing export...";
            });
            
            var projectId = _projectContext.CurrentProjectId!.Value;
            var filePath = dialog.FileName;
            
            bool success = false;
            
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => 
                    ExportStatus = $"Exporting {selectedPlugins.Count} plugin(s) to Excel...");
                
                success = await _excelExportService.ExportFindingsAsync(
                    projectId, 
                    filePath, 
                    selectedTaskKeys,
                    IncludeErrors, 
                    IncludeWarnings, 
                    IncludeInfo,
                    (pluginName, current, total) =>
                    {
                        // Update progress on UI thread
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ExportStatus = $"Exporting plugin {current} of {total}: {pluginName}";
                        });
                    });
                
                if (success)
                {
                    _logger.LogInformation(
                        "Exported {Count} plugin(s) to Excel: {FilePath}", 
                        selectedPlugins.Count, filePath);
                }
                
                // Update UI and show messages on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (success)
                    {
                        ExportStatus = "Export complete!";
                        ExportSucceeded = true;
                        
                        // Show success message
                        MessageBox.Show(
                            $"Exported to {filePath}", 
                            "Export Successful", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                        
                        _dialog.DialogResult = true;
                    }
                    else
                    {
                        ExportStatus = "Export failed";
                        MessageBox.Show(
                            "Export failed. Check logs for details.", 
                            "Export Failed", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export failed with exception");
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ExportStatus = "Export failed";
                    MessageBox.Show(
                        $"Export failed: {ex.Message}", 
                        "Export Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsExporting = false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in export dialog");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsExporting = false;
                MessageBox.Show(
                    $"An error occurred: {ex.Message}", 
                    "Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            });
        }
    }
    
    [RelayCommand]
    private void Cancel()
    {
        if (!IsExporting)
        {
            _dialog.DialogResult = false;
        }
    }
}

