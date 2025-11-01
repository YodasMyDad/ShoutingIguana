using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for the export options dialog.
/// </summary>
public partial class ExportOptionsViewModel : ObservableObject
{
    private readonly Window _dialog;
    private readonly ICsvExportService _csvExportService;
    private readonly IExcelExportService _excelExportService;
    private readonly IProjectContext _projectContext;
    private readonly ILogger<ExportOptionsViewModel> _logger;
    
    [ObservableProperty]
    private bool _includeTechnicalMetadata;
    
    [ObservableProperty]
    private string _exportFormat = "Excel";
    
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
    
    public bool ExportSucceeded { get; private set; }
    
    public ExportOptionsViewModel(
        Window dialog, 
        ICsvExportService csvExportService,
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        ILogger<ExportOptionsViewModel> logger)
    {
        _dialog = dialog;
        _csvExportService = csvExportService;
        _excelExportService = excelExportService;
        _projectContext = projectContext;
        _logger = logger;
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
            
            // Determine file filter and extension based on format
            string filter, defaultExt, fileName;
            if (ExportFormat == "CSV")
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
                if (ExportFormat == "CSV")
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => 
                        ExportStatus = "Exporting findings to CSV...");
                    
                    await _csvExportService.ExportFindingsAsync(
                        projectId, 
                        filePath, 
                        IncludeTechnicalMetadata, 
                        IncludeErrors, 
                        IncludeWarnings, 
                        IncludeInfo);
                    success = true;
                    _logger.LogInformation(
                        "Exported findings to CSV: {FilePath} (Technical: {Tech}, Errors: {Err}, Warnings: {Warn}, Info: {Info})", 
                        filePath, IncludeTechnicalMetadata, IncludeErrors, IncludeWarnings, IncludeInfo);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => 
                        ExportStatus = "Exporting findings to Excel...");
                    
                    success = await _excelExportService.ExportFindingsAsync(
                        projectId, 
                        filePath, 
                        IncludeTechnicalMetadata, 
                        IncludeErrors, 
                        IncludeWarnings, 
                        IncludeInfo);
                    
                    if (success)
                    {
                        _logger.LogInformation(
                            "Exported findings to Excel: {FilePath} (Technical: {Tech}, Errors: {Err}, Warnings: {Warn}, Info: {Info})", 
                            filePath, IncludeTechnicalMetadata, IncludeErrors, IncludeWarnings, IncludeInfo);
                    }
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

