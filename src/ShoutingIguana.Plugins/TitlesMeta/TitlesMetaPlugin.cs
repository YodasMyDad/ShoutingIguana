using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.TitlesMeta;

[Plugin(Id = "com.shoutingiguana.titlesmeta", Name = "Titles & Meta", MinSdkVersion = "0.1.0")]
public class TitlesMetaPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.titlesmeta";
    public string Name => "Titles & Meta";
    public Version Version => new(1, 0, 0);
    public string Description => "Title, meta, Open Graph, Twitter Cards, and heading structure validation";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for titles & meta analysis
        var schema = ReportSchema.Create("TitlesMeta")
            
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("Title", ReportColumnType.String, "Title")
            .AddColumn("MetaDescription", ReportColumnType.String, "Meta Description")
            .AddColumn("Length", ReportColumnType.Integer, "Length")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new TitlesMetaTask(
            context.CreateLogger(nameof(TitlesMetaTask)),
            context.GetRepositoryAccessor()));
    }
}

