using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.LinkGraph;

[Plugin(Id = "com.shoutingiguana.linkgraph", Name = "Link Graph", MinSdkVersion = "1.0.0")]
public class LinkGraphPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.linkgraph";
    public string Name => "Link Graph";
    public Version Version => new(1, 0, 0);
    public string Description => "Internal linking structure showing how pages link to each other with anchor text and link types";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new LinkGraphTask(
            context.CreateLogger(nameof(LinkGraphTask)),
            context.GetRepositoryAccessor()));
    }
}

