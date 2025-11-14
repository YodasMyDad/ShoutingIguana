using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Net;
using System.Runtime.CompilerServices;

namespace ShoutingIguana.ViewModels.Models;

/// <summary>
/// ViewModel wrapper for a dynamic report row that supports data binding.
/// Uses a dictionary for dynamic property access with indexer support.
/// </summary>
public class DynamicReportRowViewModel : DynamicObject, INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _data = new();
    
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public int? UrlId { get; set; }
    public DateTime CreatedUtc { get; set; }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Indexer for WPF binding support (allows Binding="[ColumnName]").
    /// </summary>
    public object? this[string columnName]
    {
        get => GetValue(columnName);
        set => SetValue(columnName, value);
    }

    /// <summary>
    /// Gets a column value by name.
    /// </summary>
    public object? GetValue(string columnName)
    {
        return _data.TryGetValue(columnName, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a column value by name.
    /// </summary>
    public void SetValue(string columnName, object? value)
    {
        _data[columnName] = NormalizeValue(value);
        OnPropertyChanged(columnName);
    }

    /// <summary>
    /// Gets all column names.
    /// </summary>
    public IEnumerable<string> GetColumnNames() => _data.Keys;

    /// <summary>
    /// Tries to get a dynamic member (for binding support).
    /// </summary>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = GetValue(binder.Name);
        return true;
    }

    /// <summary>
    /// Tries to set a dynamic member (for binding support).
    /// </summary>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        SetValue(binder.Name, value);
        return true;
    }

    /// <summary>
    /// Gets all dynamic member names (for binding support).
    /// </summary>
    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return _data.Keys;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Creates a ViewModel from a Core ReportRow model.
    /// </summary>
    public static DynamicReportRowViewModel FromModel(Core.Models.ReportRow row)
    {
        var vm = new DynamicReportRowViewModel
        {
            Id = row.Id,
            ProjectId = row.ProjectId,
            TaskKey = row.TaskKey,
            UrlId = row.UrlId,
            CreatedUtc = row.CreatedUtc
        };

        var data = row.GetData();
        if (data != null)
        {
            foreach (var kvp in data)
            {
                vm.SetValue(kvp.Key, kvp.Value);
            }
        }

        return vm;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is string text)
        {
            return WebUtility.HtmlDecode(text);
        }

        return value;
    }
}

