using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.InternalLinking;

[Plugin(Id = "com.shoutingiguana.internallinking", Name = "Internal Linking", MinSdkVersion = "0.1.0")]
public class InternalLinkingPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.internallinking";
    public string Name => "Internal Linking";
    public Version Version => new(1, 0, 0);
    public string Description => "Internal link analysis: inlinks, outlinks, anchor text, orphan pages, and link equity";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for internal linking analysis
        var schema = ReportSchema.Create("InternalLinking")
            
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("IssueType", ReportColumnType.String, "Issue")
            .AddColumn("FromURL", ReportColumnType.Url, "From URL")
            .AddColumn("ToURL", ReportColumnType.Url, "To URL")
            .AddColumn("AnchorText", ReportColumnType.String, "Anchor Text")
            .AddColumn("Inlinks", ReportColumnType.Integer, "Inlinks")
            .AddColumn("Outlinks", ReportColumnType.Integer, "Outlinks")
            .AddColumn("Depth", ReportColumnType.Integer, "Depth")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new InternalLinkingTask(context.CreateLogger(nameof(InternalLinkingTask))));
    }
}

