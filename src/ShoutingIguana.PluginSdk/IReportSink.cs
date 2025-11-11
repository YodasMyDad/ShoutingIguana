namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Sink for reporting custom data rows to the application.
/// Access via UrlContext.Reports in your task's ExecuteAsync method.
/// </summary>
/// <remarks>
/// <para>
/// Use this to report structured data with custom columns defined by your plugin.
/// Each plugin can define its own schema with specific columns tailored to its needs.
/// </para>
/// <para>
/// Before reporting data, you must register a schema during plugin initialization
/// using IHostContext.RegisterReportSchema().
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In plugin Initialize:
/// var schema = ReportSchema.Create("BrokenLinks")
///     .AddPrimaryColumn("SourceUrl", ReportColumnType.Url)
///     .AddColumn("BrokenUrl", ReportColumnType.Url)
///     .AddColumn("StatusCode", ReportColumnType.Integer)
///     .Build();
/// context.RegisterReportSchema(schema);
/// 
/// // In task ExecuteAsync:
/// var row = ReportRow.Create()
///     .Set("SourceUrl", ctx.Url.ToString())
///     .Set("BrokenUrl", brokenLink)
///     .Set("StatusCode", 404);
/// await ctx.Reports.ReportAsync(Key, row, ct);
/// </code>
/// </example>
public interface IReportSink
{
    /// <summary>
    /// Reports a data row to the application.
    /// </summary>
    /// <param name="taskKey">
    /// Key of the task reporting the data. Must match IUrlTask.Key and the registered schema.
    /// </param>
    /// <param name="row">
    /// Report row with column values. Column names must match the registered schema.
    /// </param>
    /// <param name="urlId">
    /// Optional URL ID if this row is associated with a specific URL.
    /// Required if schema IsUrlBased is true.
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportAsync(string taskKey, ReportRow row, int? urlId = null, CancellationToken ct = default);
}

