using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Robots;

[Plugin(Id = "com.shoutingiguana.robots", Name = "Robots & Indexability", MinSdkVersion = "1.0.0")]
public class RobotsPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.robots";
    public string Name => "Robots & Indexability";
    public Version Version => new(1, 0, 0);
    public string Description => "Robots.txt compliance, meta robots, X-Robots-Tag, and indexability analysis";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new RobotsTask(context.CreateLogger(nameof(RobotsTask))));
    }
}

