namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Represents a task that analyzes a crawled URL.
/// </summary>
public interface IUrlTask
{
    /// <summary>
    /// Unique key for this task (e.g., "BrokenLinks").
    /// Used to categorize findings.
    /// </summary>
    string Key { get; }
    
    /// <summary>
    /// Display name for this task (e.g., "Broken Links").
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Execution priority. Lower values execute first.
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Execute the task for the given URL context.
    /// </summary>
    Task ExecuteAsync(UrlContext ctx, CancellationToken ct);
}

