using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.TitlesMeta;

[Plugin(Id = "com.shoutingiguana.titlesmeta", Name = "Titles & Meta", MinSdkVersion = "1.0.0")]
public class TitlesMetaPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.titlesmeta";
    public string Name => "Titles & Meta";
    public Version Version => new(1, 0, 0);
    public string Description => "Extracts and validates page titles and meta descriptions";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new TitlesMetaTask(context.CreateLogger(nameof(TitlesMetaTask))));
    }
}

