using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.BrokenLinks;

[Plugin(Id = "com.shoutingiguana.brokenlinks", Name = "Broken Links", MinSdkVersion = "1.0.0")]
public class BrokenLinksPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.brokenlinks";
    public string Name => "Broken Links";
    public Version Version => new(1, 0, 0);
    public string Description => "Detects broken links and reports source pages";

    public void Initialize(IHostContext context)
    {
        // Get service provider from context to create checker
        var serviceProvider = context.GetServiceProvider();
        var checker = new BrokenLinksChecker(serviceProvider, context.CreateLogger<BrokenLinksChecker>());
        
        context.RegisterTask(new BrokenLinksTask(context.CreateLogger(nameof(BrokenLinksTask)), checker));
    }
}

