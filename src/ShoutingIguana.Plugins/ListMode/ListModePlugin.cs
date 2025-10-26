using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.ListMode;

[Plugin(Id = "com.shoutingiguana.listmode", Name = "List-Mode Crawling", MinSdkVersion = "1.0.0")]
public class ListModePlugin : IPlugin
{
    public string Id => "com.shoutingiguana.listmode";
    public string Name => "List-Mode Crawling";
    public Version Version => new(1, 0, 0);
    public string Description => "Import and crawl specific URL lists from CSV files with custom priorities";

    public void Initialize(IHostContext context)
    {
        // List-Mode is primarily a service, not a per-URL task
        // The actual import functionality is exposed through the Tools menu
        // No task to register here
    }
}

