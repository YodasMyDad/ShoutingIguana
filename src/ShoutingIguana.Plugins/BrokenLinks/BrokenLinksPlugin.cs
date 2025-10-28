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
        // Create checker with repository accessor
        var accessor = context.GetRepositoryAccessor();
        var checker = new BrokenLinksChecker(accessor, context.CreateLogger<BrokenLinksChecker>());
        
        bool checkExternalLinks = true; // Enable external link checking to match Screaming Frog behavior
        bool checkAnchorLinks = true;
        
        context.RegisterTask(new BrokenLinksTask(
            context.CreateLogger(nameof(BrokenLinksTask)), 
            checker, 
            checkExternalLinks, 
            checkAnchorLinks));
    }
}

