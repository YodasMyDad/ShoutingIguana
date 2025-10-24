using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private ObservableCollection<Finding> _findings = new();

    [ObservableProperty]
    private ObservableCollection<Finding> _filteredFindings = new();

    [ObservableProperty]
    private Finding? _selectedFinding;

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

    private List<Finding> _allFindings = [];

    public void LoadFindings(IEnumerable<Finding> findings)
    {
        _allFindings = findings.ToList();
        UpdateCounts();
        ApplyFilters();
    }

    partial void OnSelectedSeverityChanged(Severity? value)
    {
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
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
                (f.Url?.Address?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FilteredFindings = new ObservableCollection<Finding>(filtered.OrderByDescending(f => f.Severity).ThenByDescending(f => f.CreatedUtc));
        TotalCount = FilteredFindings.Count;
    }

    private void UpdateCounts()
    {
        ErrorCount = _allFindings.Count(f => f.Severity == Severity.Error);
        WarningCount = _allFindings.Count(f => f.Severity == Severity.Warning);
        InfoCount = _allFindings.Count(f => f.Severity == Severity.Info);
        TotalCount = _allFindings.Count;
    }

    public string TabHeader => $"{DisplayName} ({TotalCount})";
}

