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
    /// Override this to clear any per-project static data.
    /// </summary>
    public virtual void CleanupProject(int projectId)
    {
        // Default: no cleanup needed
    }
}

