namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Base class for URL tasks providing default implementations.
/// </summary>
public abstract class UrlTaskBase : IUrlTask
{
    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public virtual string Description => string.Empty;
    public virtual int Priority => 100;
    
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

