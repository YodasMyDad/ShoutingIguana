namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Sink for reporting findings discovered during URL analysis.
/// Access via <see cref="UrlContext.Findings"/> in your task's ExecuteAsync method.
/// </summary>
/// <remarks>
/// <para>
/// Use this to report SEO issues, warnings, and informational findings.
/// All reported findings are automatically included in CSV/Excel/PDF exports.
/// </para>
/// <para>
/// Findings are organized by your task's Key and displayed in the application's Findings view.
/// Users can filter, sort, and export findings.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
/// {
///     // Simple finding
///     await ctx.Findings.ReportAsync(
///         Key,
///         Severity.Warning,
///         "MISSING_H1",
///         "Page has no H1 tag",
///         null);
///     
///     // Finding with structured details
///     var details = FindingDetailsBuilder.Create()
///         .AddItem($"Page: {ctx.Url}")
///         .BeginNested("💡 Recommendation")
///             .AddItem("Add an H1 tag to improve SEO")
///         .EndNested()
///         .Build();
///         
///     await ctx.Findings.ReportAsync(
///         Key,
///         Severity.Error,
///         "BROKEN_LINK",
///         "Link returns 404",
///         details);
/// }
/// </code>
/// </example>
public interface IFindingSink
{
    /// <summary>
    /// Reports a finding with optional structured details.
    /// </summary>
    /// <param name="taskKey">
    /// Key of the task reporting the finding. Use your task's <see cref="IUrlTask.Key"/> property.
    /// </param>
    /// <param name="severity">Severity level (Error, Warning, or Info).</param>
    /// <param name="code">
    /// Finding code for categorization (e.g., "BROKEN_LINK", "MISSING_H1").
    /// Use UPPER_SNAKE_CASE convention.
    /// </param>
    /// <param name="message">
    /// Human-readable message shown in the UI.
    /// Should be concise but descriptive.
    /// </param>
    /// <param name="details">
    /// Optional structured details with hierarchical information.
    /// Use <see cref="FindingDetailsBuilder"/> to create well-formatted details.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// <b>Best practices:</b>
    /// - Use Error for critical issues that must be fixed
    /// - Use Warning for issues that should be reviewed
    /// - Use Info for informational notices or statistics
    /// - Make codes unique and descriptive
    /// - Provide actionable messages
    /// - Use FindingDetailsBuilder for complex findings
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Simple finding
    /// await ctx.Findings.ReportAsync(
    ///     Key, 
    ///     Severity.Error, 
    ///     "MISSING_TITLE", 
    ///     "Page has no title tag",
    ///     null);
    /// 
    /// // Complex finding with details
    /// var details = FindingDetailsBuilder.Create()
    ///     .AddItem($"Title: \"{title}\"")
    ///     .AddItem($"Length: {title.Length} chars")
    ///     .BeginNested("💡 Recommendations")
    ///         .AddItem("Keep titles under 60 characters")
    ///         .AddItem("Include primary keywords")
    ///     .EndNested()
    ///     .WithTechnicalMetadata("titleLength", title.Length)
    ///     .Build();
    ///     
    /// await ctx.Findings.ReportAsync(
    ///     Key,
    ///     Severity.Warning,
    ///     "TITLE_TOO_LONG",
    ///     $"Title is too long ({title.Length} chars)",
    ///     details);
    /// </code>
    /// </example>
    Task ReportAsync(string taskKey, Severity severity, string code, string message, FindingDetails? details = null);
    
    /// <summary>
    /// Report a finding (legacy overload for backward compatibility).
    /// </summary>
    /// <param name="taskKey">Key of the task reporting the finding</param>
    /// <param name="severity">Severity level</param>
    /// <param name="code">Finding code (e.g., "BROKEN_LINK_404")</param>
    /// <param name="message">Human-readable message</param>
    /// <param name="data">Optional data (will be wrapped in TechnicalMetadata for backward compatibility)</param>
    [Obsolete("Use the overload accepting FindingDetails instead. This method wraps data in TechnicalMetadata.")]
    Task ReportAsync(string taskKey, Severity severity, string code, string message, object? data);
}

/// <summary>
/// Severity levels for findings.
/// </summary>
public enum Severity
{
    /// <summary>
    /// Informational finding (e.g., redirect detected).
    /// </summary>
    Info = 0,
    
    /// <summary>
    /// Warning that should be reviewed (e.g., missing meta description).
    /// </summary>
    Warning = 1,
    
    /// <summary>
    /// Error that should be fixed (e.g., broken link).
    /// </summary>
    Error = 2
}

