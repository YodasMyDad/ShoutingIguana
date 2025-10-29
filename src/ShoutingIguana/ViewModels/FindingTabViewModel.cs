using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _infoCount;

    [ObservableProperty]
    private bool _isLoading;

    private List<Finding> _allFindings = [];

    public string TabHeader => $"{DisplayName} ({TotalCount})";

    partial void OnTotalCountChanged(int value)
    {
        _ = value; // Suppress unused parameter warning
        OnPropertyChanged(nameof(TabHeader));
    }

    public void LoadFindings(IEnumerable<Finding> findings)
    {
        IsLoading = true;
        _allFindings = findings.ToList();
        UpdateCounts();
        ApplyFilters();
        IsLoading = false;
    }

    partial void OnSelectedSeverityChanged(Severity? value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value; // Suppress unused warning - required by partial method signature
        ApplyFilters();
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

        FilteredFindings = new ObservableCollection<Finding>(filtered.OrderByDescending(f => f.Severity).ThenBy(f => f.Code).ThenByDescending(f => f.CreatedUtc));
        TotalCount = FilteredFindings.Count;
    }

    private void UpdateCounts()
    {
        ErrorCount = _allFindings.Count(f => f.Severity == Severity.Error);
        WarningCount = _allFindings.Count(f => f.Severity == Severity.Warning);
        InfoCount = _allFindings.Count(f => f.Severity == Severity.Info);
        TotalCount = _allFindings.Count;
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

