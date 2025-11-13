using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Canonical;

[Plugin(Id = "com.shoutingiguana.canonical", Name = "Canonical Validation", MinSdkVersion = "0.1.0")]
public class CanonicalPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.canonical";
    public string Name => "Canonical Validation";
    public Version Version => new(1, 0, 0);
    public string Description => "Canonical URL extraction, validation, chain detection, and cross-domain analysis";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for canonical validation
        var schema = ReportSchema.Create("Canonical")
            
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("CanonicalURL", ReportColumnType.Url, "Canonical URL")
            .AddColumn("Status", ReportColumnType.String, "Status")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new CanonicalTask(
            context.CreateLogger(nameof(CanonicalTask)),
            context.GetRepositoryAccessor()));
    }
}

