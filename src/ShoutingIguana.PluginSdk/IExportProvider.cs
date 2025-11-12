namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Provides custom export functionality for specialized file formats.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPORTANT: Most plugins do NOT need to implement this interface.</b>
/// </para>
/// <para>
/// <b>Plugin findings are AUTOMATICALLY exported to CSV/Excel/PDF.</b>
/// When you report findings via <c>ctx.Reports.ReportAsync()</c>, they are automatically
/// included in the built-in export formats. The application handles formatting,
/// grouping by plugin, and export options for you.
/// </para>
/// <para>
/// <b>Only implement IExportProvider if you need:</b>
/// - Specialized file formats (XML, JSON, binary, etc.)
/// - Custom data aggregation beyond findings
/// - Non-tabular exports (e.g., sitemap.xml, robots.txt generation)
/// - Export data that isn't findings (URL inventories, link graphs, etc.)
/// </para>
/// <para>
/// <b>Examples of when to use IExportProvider:</b>
/// - Sitemap plugin exporting sitemap.xml format
/// - Link graph plugin exporting GraphML or JSON network data
/// - Custom JSON API reports
/// - Binary data formats
/// </para>
/// <para>
/// <b>For standard tabular data, just use findings:</b>
/// The built-in CSV/Excel/PDF exports automatically include all plugin findings
/// with proper formatting, filtering, and technical metadata options.
/// </para>
/// </remarks>
/// <example>
/// <para><b>✅ You DON'T need IExportProvider for this (automatic):</b></para>
/// <code>
/// // This automatically exports to CSV/Excel/PDF:
/// await ctx.Reports.ReportAsync(
///     Key,
///     Severity.Warning,
///     "BROKEN_LINK",
///     "Link returns 404",
///     details);
/// // Done! Users can export via File → Export Findings
/// </code>
/// 
/// <para><b>❌ You DO need IExportProvider for this (specialized format):</b></para>
/// <code>
/// // Generate sitemap.xml (not a tabular format)
/// public class SitemapExporter : IExportProvider
/// {
///     public string Key => "SitemapXml";
///     public string DisplayName => "Sitemap XML";
///     public string FileExtension => ".xml";
///     
///     public async Task&lt;ExportResult&gt; ExportAsync(ExportContext ctx, CancellationToken ct)
///     {
///         // Generate XML sitemap from crawled URLs
///         // This is custom logic, not findings
///         var xml = GenerateSitemapXml(urls);
///         await File.WriteAllTextAsync(ctx.FilePath, xml, ct);
///         return new ExportResult(true);
///     }
/// }
/// 
/// // Register in Initialize()
/// context.RegisterExport(new SitemapExporter(logger, serviceProvider));
/// </code>
/// </example>
public interface IExportProvider
{
    /// <summary>
    /// Gets a unique identifier for this exporter.
    /// </summary>
    /// <remarks>
    /// Should be unique across all plugins and exporters.
    /// Convention: "{PluginName}{Format}" (e.g., "SitemapXml", "LinkGraphJson")
    /// </remarks>
    /// <example>
    /// <code>
    /// public string Key => "MyPluginJson";
    /// </code>
    /// </example>
    string Key { get; }
    
    /// <summary>
    /// Gets the display name shown to users in export dialogs.
    /// </summary>
    /// <remarks>
    /// Should clearly indicate what's being exported and in what format.
    /// Convention: "{What} ({Format})" (e.g., "Sitemap (XML)", "Link Graph (JSON)")
    /// </remarks>
    /// <example>
    /// <code>
    /// public string DisplayName => "My Plugin Data (JSON)";
    /// </code>
    /// </example>
    string DisplayName { get; }
    
    /// <summary>
    /// Gets the file extension for exported files (including the dot).
    /// </summary>
    /// <remarks>
    /// Used to filter files in save dialogs and suggest default filenames.
    /// Must include the leading dot (e.g., ".xml", ".json", ".csv")
    /// </remarks>
    /// <example>
    /// <code>
    /// public string FileExtension => ".xml";
    /// </code>
    /// </example>
    string FileExtension { get; }
    
    /// <summary>
    /// Exports data to the specified file path.
    /// </summary>
    /// <param name="ctx">
    /// Export context containing the project ID, file path, and optional data context.
    /// </param>
    /// <param name="ct">Cancellation token for async operation cancellation.</param>
    /// <returns>
    /// An <see cref="ExportResult"/> indicating success or failure with optional error message.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method should:
    /// - Query the data you need (use <c>ctx.ProjectId</c>)
    /// - Generate your custom format
    /// - Write to <c>ctx.FilePath</c>
    /// - Return <c>new ExportResult(true)</c> on success
    /// - Return <c>new ExportResult(false, errorMessage)</c> on failure
    /// </para>
    /// <para>
    /// The file path is provided by the user via a save dialog.
    /// You don't need to prompt for it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public async Task&lt;ExportResult&gt; ExportAsync(ExportContext ctx, CancellationToken ct)
    /// {
    ///     try
    ///     {
    ///         // Query your data
    ///         var data = await GetDataAsync(ctx.ProjectId, ct);
    ///         
    ///         // Serialize to your format
    ///         var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
    ///         { 
    ///             WriteIndented = true 
    ///         });
    ///         
    ///         // Write to file
    ///         await File.WriteAllTextAsync(ctx.FilePath, json, ct);
    ///         
    ///         return new ExportResult(true);
    ///     }
    ///     catch (Exception ex)
    ///     {
    ///         _logger.LogError(ex, "Export failed");
    ///         return new ExportResult(false, ex.Message);
    ///     }
    /// }
    /// </code>
    /// </example>
    Task<ExportResult> ExportAsync(ExportContext ctx, CancellationToken ct);
}

/// <summary>
/// Context for export operations, containing all information needed to perform an export.
/// </summary>
/// <param name="ProjectId">The ID of the project being exported.</param>
/// <param name="FilePath">
/// The target file path where the export should be written.
/// This is provided by the user via a save file dialog.
/// </param>
/// <param name="DataContext">
/// Optional plugin-specific data passed from the UI.
/// Can be null if no additional context is needed.
/// Cast to your expected type if you provide custom UI.
/// </param>
/// <remarks>
/// Your export implementation should:
/// - Use the ProjectId to query data from repositories
/// - Write output to the FilePath (creating parent directories if needed)
/// - Optionally use DataContext for UI-provided filtering/options
/// </remarks>
/// <example>
/// <code>
/// public async Task&lt;ExportResult&gt; ExportAsync(ExportContext ctx, CancellationToken ct)
/// {
///     // Ensure directory exists
///     var directory = Path.GetDirectoryName(ctx.FilePath);
///     if (!string.IsNullOrEmpty(directory))
///     {
///         Directory.CreateDirectory(directory);
///     }
///     
///     // Query data using ctx.ProjectId
///     var data = await GetDataAsync(ctx.ProjectId, ct);
///     
///     // Generate export
///     var output = SerializeData(data);
///     
///     // Write to file
///     await File.WriteAllTextAsync(ctx.FilePath, output, ct);
///     
///     return new ExportResult(true);
/// }
/// </code>
/// </example>
public sealed record ExportContext(
    int ProjectId,
    string FilePath,
    object DataContext);

/// <summary>
/// Result of an export operation, indicating success or failure.
/// </summary>
/// <param name="Success">
/// <c>true</c> if the export completed successfully; <c>false</c> if it failed.
/// </param>
/// <param name="ErrorMessage">
/// Optional error message if the export failed. Should be user-friendly and actionable.
/// Include details about what went wrong and potential fixes.
/// </param>
/// <remarks>
/// <para>
/// Return <c>new ExportResult(true)</c> on success.
/// </para>
/// <para>
/// Return <c>new ExportResult(false, "error description")</c> on failure.
/// The error message will be shown to the user.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Success
/// return new ExportResult(true);
/// 
/// // Failure with helpful message
/// return new ExportResult(false, "No indexable URLs found. Run a crawl first.");
/// 
/// // Failure with exception details
/// catch (Exception ex)
/// {
///     _logger.LogError(ex, "Export failed");
///     return new ExportResult(false, $"Export failed: {ex.Message}");
/// }
/// </code>
/// </example>
public sealed record ExportResult(
    bool Success,
    string? ErrorMessage = null);

