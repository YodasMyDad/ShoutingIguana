using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.StructuredData;

[Plugin(Id = "com.shoutingiguana.structureddata", Name = "Structured Data", MinSdkVersion = "0.1.0")]
public class StructuredDataPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.structureddata";
    public string Name => "Structured Data";
    public Version Version => new(1, 0, 0);
    public string Description => "JSON-LD, Microdata, and Schema.org structured data extraction and validation";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for structured data analysis
        var schema = ReportSchema.Create("StructuredData")
            
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("SchemaType", ReportColumnType.String, "Schema Type")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("Property", ReportColumnType.String, "Property")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new StructuredDataTask(context.CreateLogger(nameof(StructuredDataTask))));
    }
}

