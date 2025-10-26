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
using ShoutingIguana.Services;

namespace ShoutingIguana.ViewModels;

public partial class FindingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<FindingsViewModel> _logger;
    private readonly ICsvExportService _csvExportService;
    private readonly IExcelExportService _excelExportService;
    private readonly IProjectContext _projectContext;
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;
    private FindingTabViewModel? _previousTab;

    [ObservableProperty]
    private ObservableCollection<FindingTabViewModel> _tabs = new();

    [ObservableProperty]
    private FindingTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _detailsJson = string.Empty;

    public FindingsViewModel(
        ILogger<FindingsViewModel> logger,
        ICsvExportService csvExportService,
        IExcelExportService excelExportService,
        IProjectContext projectContext,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _csvExportService = csvExportService;
        _excelExportService = excelExportService;
        _projectContext = projectContext;
        _serviceProvider = serviceProvider;
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
            UpdateDetailsPanel();
        }

        _previousTab = value;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindingTabViewModel.SelectedFinding))
        {
            UpdateDetailsPanel();
        }
    }

    private void UpdateDetailsPanel()
    {
        if (SelectedTab?.SelectedFinding == null)
        {
            DetailsJson = string.Empty;
            return;
        }

        var finding = SelectedTab.SelectedFinding;
        
        if (string.IsNullOrEmpty(finding.DataJson))
        {
            DetailsJson = "No additional data";
        }
        else
        {
            try
            {
                // Pretty-print JSON
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(finding.DataJson);
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(jsonDoc, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch
            {
                DetailsJson = finding.DataJson;
            }
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
            
            var projectId = _projectContext.CurrentProjectId!.Value;
            var allFindings = await findingRepository.GetByProjectIdAsync(projectId);
            
            // Group findings by task key
            var grouped = allFindings.GroupBy(f => f.TaskKey);

            var tabs = new List<FindingTabViewModel>();
            
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                var tab = new FindingTabViewModel
                {
                    TaskKey = group.Key,
                    DisplayName = FormatTaskDisplayName(group.Key)
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

    private string FormatTaskDisplayName(string taskKey)
    {
        // Convert "BrokenLinks" to "Broken Links", "TitlesMeta" to "Titles & Meta", etc.
        return taskKey switch
        {
            "BrokenLinks" => "Broken Links",
            "TitlesMeta" => "Titles & Meta",
            "Inventory" => "Inventory",
            "Redirects" => "Redirects",
            _ => taskKey
        };
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return;
        }

        try
        {
            var dialog = new VistaSaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"shouting-iguana-findings-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var projectId = _projectContext.CurrentProjectId!.Value;
                await _csvExportService.ExportUrlInventoryAsync(projectId, dialog.FileName);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Exported to {dialog.FileName}", "Export Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information));
                _logger.LogInformation("Exported findings to CSV: {FilePath}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to CSV");
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        if (!_projectContext.HasOpenProject)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                MessageBox.Show("No project is open", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            return;
        }

        try
        {
            var dialog = new VistaSaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                DefaultExt = "xlsx",
                FileName = $"shouting-iguana-findings-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                var projectId = _projectContext.CurrentProjectId!.Value;
                var success = await _excelExportService.ExportFindingsAsync(projectId, dialog.FileName);
                
                if (success)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Exported to {dialog.FileName}", "Export Successful", 
                            MessageBoxButton.OK, MessageBoxImage.Information));
                    _logger.LogInformation("Exported findings to Excel: {FilePath}", dialog.FileName);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show("Export failed. Check logs for details.", "Export Failed", 
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export to Excel");
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

    public void Dispose()
    {
        if (_disposed)
            return;

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
