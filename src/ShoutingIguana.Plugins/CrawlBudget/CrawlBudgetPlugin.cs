using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.CrawlBudget;

[Plugin(Id = "com.shoutingiguana.crawlbudget", Name = "Crawl Budget", MinSdkVersion = "1.0.0")]
public class CrawlBudgetPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.crawlbudget";
    public string Name => "Crawl Budget";
    public Version Version => new(1, 0, 0);
    public string Description => "Crawl budget optimization: soft 404s, server errors, crawled but not indexed pages";

    public void Initialize(IHostContext context)
    {
        // Register custom report schema for crawl budget analysis
        var schema = ReportSchema.Create("CrawlBudget")
            .AddPrimaryColumn("Page", ReportColumnType.Url, "Page")
            .AddColumn("Issue", ReportColumnType.String, "Issue")
            .AddColumn("StatusCode", ReportColumnType.Integer, "Status")
            .AddColumn("Depth", ReportColumnType.Integer, "Depth")
            .AddColumn("Severity", ReportColumnType.String, "Severity")
            .Build();
        
        context.RegisterReportSchema(schema);
        
        context.RegisterTask(new CrawlBudgetTask(
            context.CreateLogger(nameof(CrawlBudgetTask)),
            context.GetRepositoryAccessor()));
    }
}

