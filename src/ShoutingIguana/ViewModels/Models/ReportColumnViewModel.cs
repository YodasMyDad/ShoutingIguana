using ShoutingIguana.Core.Models;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.ViewModels.Models;

/// <summary>
/// ViewModel for a report column definition.
/// </summary>
public class ReportColumnViewModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReportColumnType ColumnType { get; set; }
    public int Width { get; set; }
    public bool IsSortable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Creates a ViewModel from a Core ReportColumnDefinition.
    /// </summary>
    public static ReportColumnViewModel FromModel(ReportColumnDefinition column)
    {
        return new ReportColumnViewModel
        {
            Name = column.Name,
            DisplayName = column.DisplayName ?? column.Name,
            ColumnType = (ReportColumnType)column.ColumnType,
            Width = column.Width,
            IsSortable = column.IsSortable,
            IsFilterable = column.IsFilterable,
            IsPrimaryKey = column.IsPrimaryKey
        };
    }
}

