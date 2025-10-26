using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Sitemap;

[Plugin(Id = "com.shoutingiguana.sitemap", Name = "XML Sitemap", MinSdkVersion = "1.0.0")]
public class SitemapPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.sitemap";
    public string Name => "XML Sitemap";
    public Version Version => new(1, 0, 0);
    public string Description => "XML sitemap discovery, parsing, validation, comparison, and generation";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new SitemapTask(
            context.CreateLogger(nameof(SitemapTask)),
            context.GetServiceProvider()));
        context.RegisterExport(new SitemapExporter(
            context.CreateLogger(nameof(SitemapExporter)), 
            context.GetServiceProvider()));
    }
}

