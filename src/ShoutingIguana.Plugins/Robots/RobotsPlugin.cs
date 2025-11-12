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
        // Register custom report schema for robots/indexability analysis
        var schema = ReportSchema.Create("Robots")
            .WithVersion(2)
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("RobotsMeta", ReportColumnType.String, "Robots Meta")
            .AddColumn("XRobotsTag", ReportColumnType.String, "X-Robots-Tag")
            .AddColumn("Indexable", ReportColumnType.String, "Indexable")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new RobotsTask(context.CreateLogger(nameof(RobotsTask))));
    }
}

