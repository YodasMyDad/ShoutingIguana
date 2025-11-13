using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.ImageAudit;

[Plugin(Id = "com.shoutingiguana.imageaudit", Name = "Image Audit", MinSdkVersion = "0.1.0")]
public class ImageAuditPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.imageaudit";
    public string Name => "Image Audit";
    public Version Version => new(1, 0, 0);
    public string Description => "Comprehensive image optimization, accessibility, and performance analysis";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for image audit
        var schema = ReportSchema.Create("ImageAudit")
            
            .AddPrimaryColumn("ImageURL", ReportColumnType.Url, "Image URL")
            .AddColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("AltText", ReportColumnType.String, "Alt Text")
            .AddColumn("FileSize", ReportColumnType.Integer, "Size (bytes)")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        var logger = context.CreateLogger<ImageAuditTask>();
        context.RegisterTask(new ImageAuditTask(logger));
    }
}

