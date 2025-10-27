using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.TitlesMeta;

[Plugin(Id = "com.shoutingiguana.titlesmeta", Name = "Titles & Meta", MinSdkVersion = "1.0.0")]
public class TitlesMetaPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.titlesmeta";
    public string Name => "Titles & Meta";
    public Version Version => new(1, 0, 0);
    public string Description => "Title, meta, Open Graph, Twitter Cards, and heading structure validation";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new TitlesMetaTask(
            context.CreateLogger(nameof(TitlesMetaTask)),
            context.GetServiceProvider()));
    }
}

