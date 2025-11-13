using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Sitemap;

[Plugin(Id = "com.shoutingiguana.sitemap", Name = "XML Sitemap", MinSdkVersion = "0.1.0")]
public class SitemapPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.sitemap";
    public string Name => "XML Sitemap";
    public Version Version => new(1, 0, 0);
    public string Description => "XML sitemap discovery, parsing, validation, comparison, and generation";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for sitemap analysis
        var schema = ReportSchema.Create("Sitemap")
            
            .AddPrimaryColumn("URL", ReportColumnType.Url, "URL")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("InSitemap", ReportColumnType.String, "In Sitemap")
            .AddColumn("StatusCode", ReportColumnType.Integer, "Status")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        var accessor = context.GetRepositoryAccessor();
        
        context.RegisterTask(new SitemapTask(
            context.CreateLogger(nameof(SitemapTask)),
            accessor));
        context.RegisterExport(new SitemapExporter(
            context.CreateLogger(nameof(SitemapExporter)), 
            accessor));
    }
}

