namespace ShoutingIguana.Core.Services.Models;

/// <summary>
/// Result of a list-mode import operation.
/// </summary>
public class ListModeImportResult
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int InvalidCount { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Errors { get; set; } = [];
}

