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
        // Register custom report schema for inventory
        var schema = ReportSchema.Create("Inventory")
            .AddPrimaryColumn("URL", ReportColumnType.Url, "URL")
            .AddColumn("ContentType", ReportColumnType.String, "Content Type")
            .AddColumn("Status", ReportColumnType.Integer, "Status")
            .AddColumn("Depth", ReportColumnType.Integer, "Depth")
            .AddColumn("Indexable", ReportColumnType.String, "Indexable")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new InventoryTask());
    }
}

