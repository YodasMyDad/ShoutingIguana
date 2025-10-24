using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Redirects;

[Plugin(Id = "com.shoutingiguana.redirects", Name = "Redirects", MinSdkVersion = "1.0.0")]
public class RedirectsPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.redirects";
    public string Name => "Redirects";
    public Version Version => new(1, 0, 0);
    public string Description => "Analyzes redirect chains, loops, and canonicalization issues";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new RedirectsTask(context.CreateLogger(nameof(RedirectsTask))));
    }
}

