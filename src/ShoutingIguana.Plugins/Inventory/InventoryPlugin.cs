using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Inventory;

[Plugin(Id = "com.shoutingiguana.inventory", Name = "Inventory", MinSdkVersion = "0.1.0")]
public class InventoryPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.inventory";
    public string Name => "Inventory";
    public Version Version => new(2, 0, 0);
    public string Description => "Per-URL inventory: title, description, H1, canonical, robots, language, size, and computed indexability.";

    public void Initialize(IHostContext context)
    {
        var schema = ReportSchema.Create("Inventory")
            .AddPrimaryColumn("URL", ReportColumnType.Url, "URL")
            .AddColumn("Title", ReportColumnType.String, "Title")
            .AddColumn("Description", ReportColumnType.String, "Meta Description")
            .AddColumn("H1", ReportColumnType.String, "H1")
            .AddColumn("Canonical", ReportColumnType.String, "Canonical")
            .AddColumn("Noindex", ReportColumnType.String, "Noindex")
            .AddColumn("Nofollow", ReportColumnType.String, "Nofollow")
            .AddColumn("ContentType", ReportColumnType.String, "Content Type")
            .AddColumn("Status", ReportColumnType.Integer, "Status")
            .AddColumn("ContentLength", ReportColumnType.Integer, "Content Length")
            .AddColumn("Depth", ReportColumnType.Integer, "Depth")
            .AddColumn("Language", ReportColumnType.String, "Language")
            .AddColumn("CrawledUtc", ReportColumnType.DateTime, "Crawled (UTC)")
            .AddColumn("Indexable", ReportColumnType.String, "Indexable")
            .Build();

        context.RegisterReportSchema(schema);
        context.RegisterTask(new InventoryTask());
    }
}
