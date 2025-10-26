using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.InternalLinking;

[Plugin(Id = "com.shoutingiguana.internallinking", Name = "Internal Linking", MinSdkVersion = "1.0.0")]
public class InternalLinkingPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.internallinking";
    public string Name => "Internal Linking";
    public Version Version => new(1, 0, 0);
    public string Description => "Internal link analysis: inlinks, outlinks, anchor text, orphan pages, and link equity";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new InternalLinkingTask(context.CreateLogger(nameof(InternalLinkingTask))));
    }
}

