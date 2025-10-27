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
    /// Short description of what this task does.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Execution priority. Lower values execute first.
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Execute the task for the given URL context.
    /// </summary>
    Task ExecuteAsync(UrlContext ctx, CancellationToken ct);
    
    /// <summary>
    /// Optional cleanup method called when a project is closed.
    /// 
    /// MEMORY MANAGEMENT - CRITICAL FOR LONG-RUNNING APPLICATIONS:
    /// If your plugin maintains static state (e.g., caches, dictionaries keyed by ProjectId),
    /// you MUST implement this method to prevent memory leaks.
    /// 
    /// WHEN THIS IS CALLED:
    /// - User closes a project via File > Close Project
    /// - Application shuts down (before plugin unload)
    /// - Project is switched (old project cleanup, new project load)
    /// 
    /// WHAT TO CLEAN UP:
    /// - Static dictionaries keyed by ProjectId: _cache.Remove(projectId)
    /// - Project-specific state held in static fields
    /// - Event subscriptions tied to project lifecycle
    /// - Large data structures that won't be reused
    /// 
    /// EXAMPLE - Plugin with Static Cache:
    /// <code>
    /// private static readonly ConcurrentDictionary&lt;int, HashSet&lt;string&gt;&gt; _processedUrls = new();
    /// 
    /// public void CleanupProject(int projectId)
    /// {
    ///     _processedUrls.TryRemove(projectId, out _);
    ///     _logger.LogDebug("Cleaned up cached data for project {ProjectId}", projectId);
    /// }
    /// </code>
    /// 
    /// WARNING: Plugins that ignore this method will leak memory on each project open/close cycle.
    /// In long-running sessions, this can accumulate to significant memory usage.
    /// 
    /// Default implementation does nothing - override if you use static state.
    /// </summary>
    /// <param name="projectId">The ID of the project being closed</param>
    void CleanupProject(int projectId)
    {
        // Default: no cleanup needed
        // Override this method if your plugin maintains static state
    }
}

