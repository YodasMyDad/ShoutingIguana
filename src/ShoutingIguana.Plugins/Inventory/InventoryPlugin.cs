using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Inventory;

[Plugin(Id = "com.shoutingiguana.inventory", Name = "Inventory", MinSdkVersion = "1.0.0")]
public class InventoryPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.inventory";
    public string Name => "Inventory";
    public Version Version => new(1, 0, 0);
    public string Description => "Tracks indexability, URL structure, and orphaned pages";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new InventoryTask());
    }
}

