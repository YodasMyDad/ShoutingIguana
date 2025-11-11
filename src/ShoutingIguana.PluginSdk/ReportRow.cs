using System.Diagnostics.CodeAnalysis;

namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Represents a single row of data in a plugin report.
/// Uses a dictionary-based approach for flexible column values.
/// </summary>
public class ReportRow
{
    private readonly Dictionary<string, object?> _data = new();
    
    /// <summary>
    /// Gets all column values in this row.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Data => _data;
    
    /// <summary>
    /// Creates a new empty report row.
    /// </summary>
    public static ReportRow Create()
    {
        return new ReportRow();
    }
    
    /// <summary>
    /// Sets a column value.
    /// </summary>
    public ReportRow Set(string columnName, object? value)
    {
        _data[columnName] = value;
        return this;
    }
    
    /// <summary>
    /// Gets a column value.
    /// </summary>
    public object? Get(string columnName)
    {
        return _data.TryGetValue(columnName, out var value) ? value : null;
    }
    
    /// <summary>
    /// Gets a typed column value.
    /// </summary>
    public T? GetValue<T>(string columnName)
    {
        if (!_data.TryGetValue(columnName, out var value))
            return default;
        
        if (value is T typedValue)
            return typedValue;
        
        // Attempt conversion
        try
        {
            return (T?)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }
    
    /// <summary>
    /// Tries to get a typed column value.
    /// </summary>
    public bool TryGetValue<T>(string columnName, [NotNullWhen(true)] out T? value)
    {
        if (_data.TryGetValue(columnName, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }
        
        value = default;
        return false;
    }
    
    /// <summary>
    /// Checks if a column exists in this row.
    /// </summary>
    public bool HasColumn(string columnName)
    {
        return _data.ContainsKey(columnName);
    }
    
    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => _data.Count;
}

