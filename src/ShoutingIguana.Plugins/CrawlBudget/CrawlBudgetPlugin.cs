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
        context.RegisterTask(new CrawlBudgetTask(
            context.CreateLogger(nameof(CrawlBudgetTask)),
            context.GetRepositoryAccessor()));
    }
}

