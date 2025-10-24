namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Provides export functionality for plugin findings.
/// </summary>
public interface IExportProvider
{
    /// <summary>
    /// Unique key for this exporter (e.g., "BrokenLinksCsv").
    /// </summary>
    string Key { get; }
    
    /// <summary>
    /// Display name (e.g., "Broken Links (CSV)").
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// File extension (e.g., ".csv" or ".xlsx").
    /// </summary>
    string FileExtension { get; }
    
    /// <summary>
    /// Export findings to file.
    /// </summary>
    Task<ExportResult> ExportAsync(ExportContext ctx, CancellationToken ct);
}

/// <summary>
/// Context for export operations.
/// </summary>
public sealed record ExportContext(
    int ProjectId,
    string FilePath,
    object DataContext); // Plugin-specific data passed from UI

/// <summary>
/// Result of an export operation.
/// </summary>
public sealed record ExportResult(
    bool Success,
    string? ErrorMessage = null);

