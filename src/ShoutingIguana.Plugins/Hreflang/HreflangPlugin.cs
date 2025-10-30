using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Hreflang;

[Plugin(Id = "com.shoutingiguana.hreflang", Name = "International (Hreflang)", MinSdkVersion = "1.0.0")]
public class HreflangPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.hreflang";
    public string Name => "International (Hreflang)";
    public Version Version => new(1, 0, 0);
    public string Description => "Validates hreflang implementation for multi-language and multi-region sites";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new HreflangTask(
            context.CreateLogger(nameof(HreflangTask))));
    }
}

