namespace ShoutingIguana.PluginSdk;

/// <summary>
/// Base class for URL tasks providing default implementations.
/// </summary>
public abstract class UrlTaskBase : IUrlTask
{
    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public virtual int Priority => 100;
    
    public abstract Task ExecuteAsync(UrlContext ctx, CancellationToken ct);
}

