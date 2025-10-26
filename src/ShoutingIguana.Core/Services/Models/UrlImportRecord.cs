namespace ShoutingIguana.Core.Services.Models;

/// <summary>
/// Record for CSV import (supports URL only or URL with priority).
/// </summary>
public class UrlImportRecord
{
    public string Url { get; set; } = string.Empty;
    public int? Priority { get; set; }
}

