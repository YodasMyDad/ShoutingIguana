using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShoutingIguana.Core.Models;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.ViewModels;

/// <summary>
/// ViewModel for a single findings tab (one per plugin).
/// </summary>
public partial class FindingTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _taskKey = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Finding> _findings = new();

    [ObservableProperty]
    private ObservableCollection<Finding> _filteredFindings = new();

    [ObservableProperty]
    private Finding? _selectedFinding;
    
    [ObservableProperty]
    private FindingDetails? _selectedFindingDetails;
    
    [ObservableProperty]
    private bool _hasTechnicalMetadata;
    
    [ObservableProperty]
    private string _technicalMetadataJson = string.Empty;

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

    private List<Finding> _allFindings = [];
    private List<Finding> _currentFilteredSet = [];
    private int _currentPage = 0;
    private const int PageSize = 100;
    private Func<Task<IEnumerable<Finding>>>? _lazyLoadFunc;
    private readonly object _loadLock = new();
    private Task? _loadingTask;

    public string TabHeader => DisplayName;

    /// <summary>
    /// Set up lazy loading function without loading data yet
    /// </summary>
    public void SetLazyLoadFunction(Func<Task<IEnumerable<Finding>>> loadFunc)
    {
        _lazyLoadFunc = loadFunc;
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
                // Already loading, capture task to await outside lock
                existingTask = _loadingTask;
            }
            else if (IsDataLoaded || _lazyLoadFunc == null)
            {
                // Need to reset IsLoading on UI thread
                _ = Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
                return;
            }
            else
            {
                // Create new loading task
                _loadingTask = LoadDataAsync();
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

    private async Task LoadDataAsync()
    {
        // IsLoading already set to true in EnsureDataLoadedAsync
        
        try
        {
            var findings = await _lazyLoadFunc!();
            
            // Do ALL expensive work in background thread
            var (allFindings, sortedFindings, firstPage) = await Task.Run(() =>
            {
                var allList = findings.ToList();
                
                // Sort and filter (expensive operation)
                var sorted = allList
                    .OrderByDescending(f => f.Severity)
                    .ThenBy(f => f.Code)
                    .ThenByDescending(f => f.CreatedUtc)
                    .ToList();
                
                // Get first page
                var page = sorted.Take(PageSize).ToList();
                
                return (allList, sorted, page);
            });
            
            // Only update UI elements on UI thread (fast operation - just assignments)
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allFindings = allFindings;
                _currentFilteredSet = sortedFindings;
                _currentPage = 0;
                TotalCount = allFindings.Count;
                
                FilteredFindings = new ObservableCollection<Finding>(firstPage);
                HasMoreItems = _currentFilteredSet.Count > PageSize;
                
                IsDataLoaded = true;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading findings for {DisplayName}: {ex.Message}");
            // Set IsDataLoaded to false so user can retry
            IsDataLoaded = false;
            throw;
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Load findings immediately (for backwards compatibility or when data is already available)
    /// </summary>
    public async Task LoadFindingsAsync(IEnumerable<Finding> findings)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
        
        try
        {
            // Offload list creation to background thread to avoid blocking UI
            _allFindings = await Task.Run(() => findings.ToList());
            
            // Apply filters on UI thread since it modifies ObservableCollections
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyFilters();
                IsDataLoaded = true;
            });
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }

    partial void OnSelectedSeverityChanged(Severity? value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        _ = ApplyFiltersAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        _ = ApplyFiltersAsync();
    }
    
    partial void OnSelectedFindingChanged(Finding? value)
    {
        if (value != null)
        {
            // Parse the FindingDetails from the JSON
            SelectedFindingDetails = value.GetDetails();
            
            // Check if there's technical metadata
            HasTechnicalMetadata = SelectedFindingDetails?.TechnicalMetadata != null && 
                                   SelectedFindingDetails.TechnicalMetadata.Count > 0;
            
            // Format technical metadata JSON for display
            if (HasTechnicalMetadata && SelectedFindingDetails?.TechnicalMetadata != null)
            {
                try
                {
                    TechnicalMetadataJson = JsonSerializer.Serialize(
                        SelectedFindingDetails.TechnicalMetadata,
                        new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    TechnicalMetadataJson = "Error formatting technical metadata";
                }
            }
            else
            {
                TechnicalMetadataJson = string.Empty;
            }
        }
        else
        {
            SelectedFindingDetails = null;
            HasTechnicalMetadata = false;
            TechnicalMetadataJson = string.Empty;
        }
    }

    private async Task ApplyFiltersAsync()
    {
        // Show loading state
        await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);
        
        try
        {
            // Capture filter values for Task.Run
            var selectedSeverity = SelectedSeverity;
            var searchText = SearchText;
            
            // Do expensive filtering and sorting in background
            var (filteredSet, firstPage) = await Task.Run(() =>
            {
                var filtered = _allFindings.AsEnumerable();

                // Filter by severity
                if (selectedSeverity.HasValue)
                {
                    filtered = filtered.Where(f => f.Severity == selectedSeverity.Value);
                }

                // Filter by search text
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filtered = filtered.Where(f =>
                        f.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        f.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        f.Url.Address.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                }

                // Store the filtered and sorted set
                var sorted = filtered.OrderByDescending(f => f.Severity).ThenBy(f => f.Code).ThenByDescending(f => f.CreatedUtc).ToList();
                var page = sorted.Take(PageSize).ToList();
                
                return (sorted, page);
            });
            
            // Update UI on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _currentFilteredSet = filteredSet;
                _currentPage = 0;
                TotalCount = _currentFilteredSet.Count;
                
                FilteredFindings = new ObservableCollection<Finding>(firstPage);
                HasMoreItems = _currentFilteredSet.Count > PageSize;
            });
        }
        finally
        {
            // Must set IsLoading on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() => IsLoading = false);
        }
    }
    
    // Keep synchronous version for initial load only
    private void ApplyFilters()
    {
        var filtered = _allFindings.AsEnumerable();

        // Filter by severity
        if (SelectedSeverity.HasValue)
        {
            filtered = filtered.Where(f => f.Severity == SelectedSeverity.Value);
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(f =>
                f.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                f.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                f.Url.Address.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        // Note: This is only called during initial background load in LoadDataAsync
        // So it's already on a background thread
        _currentFilteredSet = filtered.OrderByDescending(f => f.Severity).ThenBy(f => f.Code).ThenByDescending(f => f.CreatedUtc).ToList();
        TotalCount = _currentFilteredSet.Count;
        
        // Load only first page
        _currentPage = 0;
        var firstPage = _currentFilteredSet.Take(PageSize).ToList();
        FilteredFindings = new ObservableCollection<Finding>(firstPage);
        HasMoreItems = _currentFilteredSet.Count > PageSize;
    }

    [RelayCommand]
    private void LoadNextPage()
    {
        if (IsLoadingMore || !HasMoreItems) return;
        
        IsLoadingMore = true;
        _currentPage++;
        
        var nextItems = _currentFilteredSet
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .ToList();
        
        foreach (var item in nextItems)
        {
            FilteredFindings.Add(item);
        }
        
        HasMoreItems = (_currentPage + 1) * PageSize < _currentFilteredSet.Count;
        IsLoadingMore = false;
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (SelectedFinding?.Url.Address != null)
        {
            Clipboard.SetText(SelectedFinding.Url.Address);
        }
    }

    [RelayCommand]
    private void CopyMessage()
    {
        if (SelectedFinding?.Message != null)
        {
            Clipboard.SetText(SelectedFinding.Message);
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (SelectedFinding?.Url.Address != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedFinding.Url.Address,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Silently fail if browser cannot be opened
            }
        }
    }
}

