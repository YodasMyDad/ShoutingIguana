using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.DuplicateContent;

[Plugin(Id = "com.shoutingiguana.duplicatecontent", Name = "Duplicate Content", MinSdkVersion = "0.1.0")]
public class DuplicateContentPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.duplicatecontent";
    public string Name => "Duplicate Content";
    public Version Version => new(1, 0, 0);
    public string Description => "Exact and near-duplicate content detection using SHA-256 and SimHash algorithms, plus domain/protocol variant validation";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for duplicate content analysis
        var schema = ReportSchema.Create("DuplicateContent")
            
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("DuplicateOf", ReportColumnType.Url, "Duplicate Of")
            .AddColumn("Similarity", ReportColumnType.Integer, "Similarity %")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new DuplicateContentTask(
            context.CreateLogger(nameof(DuplicateContentTask)),
            context.GetRepositoryAccessor()));
    }
}

