using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Services;
using ShoutingIguana.PluginSdk;
using ShoutingIguana.ViewModels.Models;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for a single findings tab (one per plugin).
/// Handles dynamic ReportRow-based data exclusively.
/// </summary>
public partial class FindingTabViewModel : ObservableObject
{
    private const string CustomExtractionTaskKey = "CustomExtraction";

    [ObservableProperty]
    private string _taskKey = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    // Dynamic report support
    [ObservableProperty]
    private ObservableCollection<ReportColumnViewModel> _reportColumns = new();
    
    [ObservableProperty]
    private ObservableCollection<DynamicReportRowViewModel> _reportRows = new();
    
    [ObservableProperty]
    private DynamicReportRowViewModel? _selectedReportRow;
    
    private Core.Models.ReportSchema? _schema;
    
    /// <summary>
    /// Gets the count of visible items.
    /// </summary>
    public int VisibleItemCount => ReportRows.Count;
    
    [ObservableProperty]
    private FindingDetails? _selectedFindingDetails;

    [ObservableProperty]
    private Severity? _selectedSeverity;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _hasMoreItems;

    [ObservableProperty]
    private bool _isDataLoaded;

    private bool _isCustomExtractionReport;
    private List<DynamicReportRowViewModel> _customExtractionAllRows = [];

    private int _projectId;
    private int _currentPage = 0;
    private const int PageSize = 100;
    private Func<Task>? _lazyLoadDynamicFunc;
    private readonly object _loadLock = new();
    private Task? _loadingTask;

    public string TabHeader => DisplayName;
    
    partial void OnReportRowsChanged(ObservableCollection<DynamicReportRowViewModel> value)
    {
        _ = value;
        OnPropertyChanged(nameof(VisibleItemCount));
    }
    
    /// <summary>
    /// Set up lazy loading function for new dynamic Report-based reports
    /// </summary>
    public void SetDynamicLazyLoadFunction(int projectId, Func<Task> loadFunc)
    {
        _projectId = projectId;
        _lazyLoadDynamicFunc = loadFunc;
        IsDataLoaded = false;
    }

    /// <summary>
    /// Load findings data (called on-demand when tab is selected)
    /// </summary>
    public async Task EnsureDataLoadedAsync()
    {
        // Check if already loaded
        if (IsDataLoaded) return;
        
        // CRITICAL: Set IsLoading on UI thread FIRST so skeleton shows immediately
        // Use BeginInvoke (fire-and-forget) to not block
        _ = Application.Current.Dispatcher.BeginInvoke(() => IsLoading = true, System.Windows.Threading.DispatcherPriority.Send);
        
        // Yield to allow UI to update
        await Task.Delay(1); // Short delay to ensure UI updates
        
        Task? existingTask;
        
        // Prevent race condition - only one load at a time
        lock (_loadLock)
        {
            if (_loadingTask != null)
            {
                existingTask = _loadingTask;
            }
            else if (IsDataLoaded || _lazyLoadDynamicFunc == null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
                return;
            }
            else
            {
                _loadingTask = LoadDynamicDataAsync();
                existingTask = _loadingTask;
            }
        }
        
        // Await outside the lock
        try
        {
            await existingTask;
        }
        finally
        {
            lock (_loadLock)
            {
                // Only clear if this is still the same task
                if (_loadingTask == existingTask)
                {
                    _loadingTask = null;
                }
            }
        }
    }

    private async Task LoadDynamicDataAsync()
    {
        // IsLoading already set to true in EnsureDataLoadedAsync
        
        try
        {
            // Call the lazy load function which will load schema and initial data
            if (_lazyLoadDynamicFunc != null)
            {
                await _lazyLoadDynamicFunc();
                await Application.Current.Dispatcher.InvokeAsync(() => IsDataLoaded = true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading dynamic report data for {DisplayName}: {ex.Message}");
            IsDataLoaded = false;
            throw;
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    partial void OnSelectedSeverityChanged(Severity? value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        _ = ApplyDynamicFiltersAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        _ = ApplyDynamicFiltersAsync();
    }
    
    partial void OnSelectedReportRowChanged(DynamicReportRowViewModel? value)
    {
        if (value == null)
        {
            SelectedFindingDetails = null;
            return;
        }

        var columns = GetActiveReportColumns().ToList();
        var detailItems = new List<string>();
        var explanationValue = value.GetValue("Explanation");

        if (columns.Count == 0)
        {
            var columnNames = value.GetColumnNames().ToList();
            foreach (var columnName in columnNames)
            {
                if (string.Equals(columnName, "Explanation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = value.GetValue(columnName);
                var formatted = rawValue?.ToString() ?? "(none)";
                detailItems.Add($"{columnName}: {formatted}");
            }
        }
        else
        {
            foreach (var column in columns)
            {
                var rawValue = value.GetValue(column.Name);
                var formatted = FormatColumnValue(rawValue, column.ColumnType);
                detailItems.Add($"{column.DisplayName}: {formatted}");
            }
        }

        if (explanationValue != null && !string.IsNullOrWhiteSpace(explanationValue.ToString()))
        {
            var formatted = explanationValue.ToString() ?? "(none)";
            detailItems.Add($"Description: {formatted}");
        }

        SelectedFindingDetails = new FindingDetails
        {
            Items = detailItems
        };
    }

    [RelayCommand]
    private async Task LoadNextPageAsync()
    {
        if (IsLoadingMore || !HasMoreItems) return;
        
        IsLoadingMore = true;
        _currentPage++;
        
        try
        {
            await LoadNextDynamicPageAsync();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }
    
    private async Task LoadNextDynamicPageAsync()
    {
        if (_schema == null || _isCustomExtractionReport) return;
        
        try
        {
            // Create a new scope for this paging request
            var host = ((App)Application.Current).ServiceHost;
            if (host == null)
            {
                Debug.WriteLine("ServiceHost is null - cannot load next page");
                return;
            }
            
            using var scope = host.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IReportDataRepository>();
            
            // Load next page from database
            var nextRows = await repository.GetByTaskKeyAsync(_projectId, _schema.TaskKey, page: _currentPage, pageSize: PageSize);
            var rowVms = nextRows.Select(DynamicReportRowViewModel.FromModel).ToList();
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var row in rowVms)
                {
                    ReportRows.Add(row);
                }
                
                HasMoreItems = ReportRows.Count < TotalCount;
                OnPropertyChanged(nameof(VisibleItemCount)); // Update count for UI
                
                Debug.WriteLine($"[{DisplayName}] Loaded page {_currentPage}: Added {rowVms.Count} rows, total now {ReportRows.Count}/{TotalCount}");
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading next page for {DisplayName}: {ex.Message}");
        }
    }
    
    private async Task ApplyDynamicFiltersAsync()
    {
        if (_schema == null) return;
        
        if (_isCustomExtractionReport)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
            
            try
            {
                var filtered = FilterCustomExtractionRows();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ReportRows = new ObservableCollection<DynamicReportRowViewModel>(filtered);
                    TotalCount = filtered.Count;
                    HasMoreItems = false;
                    _currentPage = 0;
                    OnPropertyChanged(nameof(VisibleItemCount));
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
            }
            
            return;
        }
        
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
        
        try
        {
            // Create a new scope for filtering
            var host = ((App)Application.Current).ServiceHost;
            if (host == null)
            {
                Debug.WriteLine("ServiceHost is null - cannot apply filters");
                await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
                return;
            }
            
            using var scope = host.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Core.Repositories.IReportDataRepository>();
            
            // For now, reload first page with filters (can be optimized later with in-memory filtering)
            var reportRows = await repository.GetByTaskKeyAsync(_projectId, _schema.TaskKey, page: 0, pageSize: PageSize, searchText: SearchText);
            
            // Client-side severity filtering (since it's stored as string in JSON)
            var rowVms = reportRows.Select(DynamicReportRowViewModel.FromModel).ToList();
            
            if (SelectedSeverity.HasValue)
            {
                var severityFilter = SelectedSeverity.Value.ToString();
                rowVms = rowVms.Where(r => r.GetValue("Severity")?.ToString() == severityFilter).ToList();
            }
            
            var totalCount = await repository.GetCountByTaskKeyAsync(_projectId, _schema.TaskKey, SearchText);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReportRows = new ObservableCollection<DynamicReportRowViewModel>(rowVms);
                TotalCount = totalCount;
                HasMoreItems = totalCount > PageSize;
                _currentPage = 0;
                OnPropertyChanged(nameof(VisibleItemCount)); // Notify for empty state binding
            });
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }
    
    /// <summary>
    /// Loads dynamic report data with schema from the database.
    /// </summary>
    public async Task LoadDynamicReportAsync(Core.Models.ReportSchema schema, Core.Repositories.IReportDataRepository repository, int projectId)
    {
        try
        {
            _schema = schema;
            _projectId = projectId;
            _isCustomExtractionReport = false;
            _customExtractionAllRows = [];
            
            // Load columns from schema
            var columnDefs = schema.GetColumns();
            if (columnDefs != null)
            {
                var columnVms = columnDefs.Select(ReportColumnViewModel.FromModel).ToList();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ReportColumns = new ObservableCollection<ReportColumnViewModel>(columnVms);
                });
            }
            
            // Load first page of data from database
            var reportRows = await repository.GetByTaskKeyAsync(projectId, schema.TaskKey, page: 0, pageSize: PageSize);
            var rowVms = reportRows.Select(DynamicReportRowViewModel.FromModel).ToList();
            
            // Get total count
            var totalCount = await repository.GetCountByTaskKeyAsync(projectId, schema.TaskKey);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReportRows = new ObservableCollection<DynamicReportRowViewModel>(rowVms);
                TotalCount = totalCount;
                HasMoreItems = totalCount > PageSize;
                _currentPage = 0;
                OnPropertyChanged(nameof(VisibleItemCount)); // Notify for empty state binding
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading dynamic report for {DisplayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Loads custom extraction data and pivots it into a per-page dataset.
    /// </summary>
    public async Task LoadCustomExtractionReportAsync(
        Core.Models.ReportSchema schema,
        Core.Repositories.IReportDataRepository reportRepository,
        Core.Repositories.ICustomExtractionRuleRepository ruleRepository,
        int projectId)
    {
        try
        {
            _schema = schema;
            _projectId = projectId;
            _isCustomExtractionReport = true;

            var pivot = await CustomExtractionPivotBuilder
                .BuildAsync(projectId, schema.TaskKey, reportRepository, ruleRepository)
                .ConfigureAwait(false);

            var columnVms = BuildCustomExtractionColumns(pivot.Columns);
            var rowVms = BuildCustomExtractionRows(pivot);

            _customExtractionAllRows = rowVms;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReportColumns = new ObservableCollection<ReportColumnViewModel>(columnVms);
                ReportRows = new ObservableCollection<DynamicReportRowViewModel>(rowVms);
                TotalCount = rowVms.Count;
                HasMoreItems = false;
                _currentPage = 0;
                OnPropertyChanged(nameof(VisibleItemCount));
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading custom extraction report for {DisplayName}: {ex.Message}");
            throw;
        }
    }
    
    private IEnumerable<ReportColumnViewModel> GetActiveReportColumns()
    {
        if (ReportColumns?.Count > 0)
        {
            return ReportColumns;
        }

        var schemaColumns = _schema?.GetColumns();
        if (schemaColumns == null)
        {
            return Enumerable.Empty<ReportColumnViewModel>();
        }

        return schemaColumns.Select(ReportColumnViewModel.FromModel);
    }

    private static List<ReportColumnViewModel> BuildCustomExtractionColumns(
        IReadOnlyList<CustomExtractionPivotBuilder.PivotColumn> columns)
    {
        var result = new List<ReportColumnViewModel>
        {
            new()
            {
                Name = "Page",
                DisplayName = "Page",
                ColumnType = ReportColumnType.Url,
                Width = 400,
                IsSortable = true,
                IsFilterable = true,
                IsPrimaryKey = true
            }
        };

        foreach (var column in columns)
        {
            result.Add(new ReportColumnViewModel
            {
                Name = column.Key,
                DisplayName = column.DisplayName,
                ColumnType = ReportColumnType.String,
                Width = 250,
                IsSortable = true,
                IsFilterable = true
            });
        }

        return result;
    }

    private static List<DynamicReportRowViewModel> BuildCustomExtractionRows(
        CustomExtractionPivotBuilder.PivotResult pivot)
    {
        var rows = new List<DynamicReportRowViewModel>();
        var taskKey = CustomExtractionTaskKey;

        foreach (var row in pivot.Rows)
        {
            var vm = new DynamicReportRowViewModel
            {
                Id = rows.Count + 1,
                TaskKey = taskKey
            };

            vm.SetValue("Page", row.Page);
            vm.SetValue("Severity", row.Severity);

            foreach (var column in pivot.Columns)
            {
                row.Values.TryGetValue(column.Key, out var value);
                vm.SetValue(column.Key, value ?? string.Empty);
            }

            rows.Add(vm);
        }

        return rows;
    }

    private List<DynamicReportRowViewModel> FilterCustomExtractionRows()
    {
        IEnumerable<DynamicReportRowViewModel> query = _customExtractionAllRows;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(row => RowContainsSearch(row, SearchText));
        }

        if (SelectedSeverity.HasValue)
        {
            var severityText = SelectedSeverity.Value.ToString();
            query = query.Where(row =>
                string.Equals(row.GetValue("Severity")?.ToString(), severityText, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static bool RowContainsSearch(DynamicReportRowViewModel row, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        foreach (var columnName in row.GetColumnNames())
        {
            var value = row.GetValue(columnName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value) &&
                value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatColumnValue(object? value, ReportColumnType columnType)
    {
        if (value == null)
        {
            return "(none)";
        }

        return columnType switch
        {
            ReportColumnType.Boolean => value is bool b ? (b ? "Yes" : "No") : value.ToString() ?? "(none)",
            ReportColumnType.DateTime => FormatDateTimeValue(value),
            ReportColumnType.Integer or ReportColumnType.Decimal => value.ToString() ?? "(none)",
            _ => value.ToString() ?? "(none)"
        };
    }

    private static string FormatDateTimeValue(object value)
    {
        if (value is DateTime dt)
        {
            return dt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        if (value is string str &&
            DateTime.TryParse(str, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        return value.ToString() ?? "(none)";
    }

    [RelayCommand]
    private void CopyUrl()
    {
        var url = GetUrlFromSelectedRow();
        if (!string.IsNullOrWhiteSpace(url))
        {
            Clipboard.SetText(url);
        }
    }

    [RelayCommand]
    private void CopyMessage()
    {
        var message = GetMessageFromSelectedRow();
        if (!string.IsNullOrWhiteSpace(message))
        {
            Clipboard.SetText(message);
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        var url = GetUrlFromSelectedRow();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Silently fail if browser cannot be opened
        }
    }

    private string? GetUrlFromSelectedRow()
    {
        if (SelectedReportRow == null)
        {
            return null;
        }

        var columns = GetActiveReportColumns().ToList();
        var urlColumn = columns.FirstOrDefault(c => c.ColumnType == ReportColumnType.Url);
        if (urlColumn != null)
        {
            var value = SelectedReportRow.GetValue(urlColumn.Name);
            if (value is string direct && !string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
        }

        var fallbackNames = new[] { "Page", "Url", "URL", "LinkedFrom", "BrokenLink", "TargetUrl" };
        foreach (var candidate in fallbackNames)
        {
            var value = SelectedReportRow.GetValue(candidate);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var fuzzy = columns.FirstOrDefault(c =>
            c.Name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("page", StringComparison.OrdinalIgnoreCase));

        if (fuzzy != null)
        {
            return SelectedReportRow.GetValue(fuzzy.Name)?.ToString();
        }

        return null;
    }

    private string? GetMessageFromSelectedRow()
    {
        if (SelectedReportRow == null)
        {
            return null;
        }

        var columns = GetActiveReportColumns().ToList();
        var messageColumn = columns.FirstOrDefault(c =>
            c.Name.Contains("message", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("description", StringComparison.OrdinalIgnoreCase));

        if (messageColumn != null)
        {
            var value = SelectedReportRow.GetValue(messageColumn.Name);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var fallbackNames = new[] { "Message", "Issue", "Description", "Notes" };
        foreach (var candidate in fallbackNames)
        {
            var value = SelectedReportRow.GetValue(candidate);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // Fallback to the first detail item if available
        return SelectedFindingDetails?.Items.FirstOrDefault();
    }
}

