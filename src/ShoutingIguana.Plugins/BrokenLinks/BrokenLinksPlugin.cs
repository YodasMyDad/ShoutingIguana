using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.BrokenLinks;

[Plugin(Id = "com.shoutingiguana.brokenlinks", Name = "Broken Links", MinSdkVersion = "1.0.0")]
public class BrokenLinksPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.brokenlinks";
    public string Name => "Broken Links";
    public Version Version => new(1, 0, 0);
    public string Description => "Detects broken internal/external links, resources, and soft 404s with detailed diagnostics";

    public void Initialize(IHostContext context)
    {
        // Get service provider from context to create checker
        var serviceProvider = context.GetServiceProvider();
        var checker = new BrokenLinksChecker(serviceProvider, context.CreateLogger<BrokenLinksChecker>());
        
        bool checkExternalLinks = false;
        bool checkAnchorLinks = true;
        
        context.RegisterTask(new BrokenLinksTask(
            context.CreateLogger(nameof(BrokenLinksTask)), 
            checker, 
            checkExternalLinks, 
            checkAnchorLinks));
    }
}

