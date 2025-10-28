namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Base class for URL tasks providing default implementations.
/// Extend this class to create your plugin's analysis task with minimal boilerplate.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides sensible defaults for Description and Priority,
/// while requiring you to implement only the essential members (Key, DisplayName, ExecuteAsync).
/// </para>
/// <para>
/// Override <see cref="CleanupProject"/> if your task uses static state that needs cleanup.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MyTask : UrlTaskBase
/// {
///     public override string Key => "MyCheck";
///     public override string DisplayName => "My SEO Check";
///     public override string Description => "Analyzes pages for custom issues";
///     
///     public override async Task ExecuteAsync(UrlContext ctx, CancellationToken ct)
///     {
///         // Your analysis logic here
///     }
/// }
/// </code>
/// </example>
public abstract class UrlTaskBase : IUrlTask
{
    /// <summary>
    /// Gets the unique key for this task. Used to categorize findings.
    /// </summary>
    /// <remarks>
    /// Should be unique across all tasks. Convention: PascalCase without spaces (e.g., "BrokenLinks", "TitlesMeta").
    /// </remarks>
    public abstract string Key { get; }
    
    /// <summary>
    /// Gets the display name shown to users in the UI.
    /// </summary>
    /// <remarks>
    /// User-friendly name with proper spacing (e.g., "Broken Links", "Titles &amp; Meta").
    /// </remarks>
    public abstract string DisplayName { get; }
    
    /// <summary>
    /// Gets a short description of what this task does.
    /// </summary>
    /// <remarks>
    /// Default implementation returns empty string. Override to provide a helpful description.
    /// </remarks>
    public virtual string Description => string.Empty;
    
    /// <summary>
    /// Gets the execution priority. Lower values execute first.
    /// </summary>
    /// <remarks>
    /// Default is 100. Set lower (e.g., 10-30) for tasks that should run early,
    /// or higher (e.g., 150-200) for tasks that depend on others.
    /// </remarks>
    public virtual int Priority => 100;
    
    /// <summary>
    /// Executes the task for the given URL context.
    /// </summary>
    /// <param name="ctx">Context containing URL, HTML, metadata, and services for reporting findings.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method is called for each crawled URL. Implement your analysis logic here.
    /// Return early for non-applicable URLs (check status code, content type, etc.)
    /// </para>
    /// <para>
    /// Report findings using <c>await ctx.Findings.ReportAsync()</c>.
    /// </para>
    /// </remarks>
    public abstract Task ExecuteAsync(UrlContext ctx, CancellationToken ct);
    
    /// <summary>
    /// Optional cleanup method called when a project is closed.
    /// 
    /// MEMORY MANAGEMENT - CRITICAL:
    /// Override this method if your task maintains static state keyed by ProjectId.
    /// Failure to clean up static state will cause memory leaks in long-running applications.
    /// 
    /// See <see cref="IUrlTask.CleanupProject"/> for detailed documentation and examples.
    /// </summary>
    /// <param name="projectId">The ID of the project being closed</param>
    public virtual void CleanupProject(int projectId)
    {
        // Default: no cleanup needed
        // Override if your task uses static fields/caches
    }
}

