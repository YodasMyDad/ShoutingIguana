using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.CustomExtraction;

[Plugin(Id = "com.shoutingiguana.customextraction", Name = "Custom Extraction", MinSdkVersion = "1.0.0")]
public class CustomExtractionPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.customextraction";
    public string Name => "Custom Extraction";
    public Version Version => new(1, 0, 0);
    public string Description => "User-defined data extraction using CSS selectors, XPath, and Regex patterns";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for custom extraction
        var schema = ReportSchema.Create("CustomExtraction")
            .WithVersion(2)
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("RuleName", ReportColumnType.String, "Rule Name")
            .AddColumn("ExtractedValue", ReportColumnType.String, "Extracted Value")
            .AddColumn("Selector", ReportColumnType.String, "Selector")
            .AddColumn("Count", ReportColumnType.Integer, "Count")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new CustomExtractionTask(
            context.CreateLogger(nameof(CustomExtractionTask)),
            context.GetRepositoryAccessor()));
    }
}

