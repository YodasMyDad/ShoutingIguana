using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.DuplicateContent;

[Plugin(Id = "com.shoutingiguana.duplicatecontent", Name = "Duplicate Content", MinSdkVersion = "1.0.0")]
public class DuplicateContentPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.duplicatecontent";
    public string Name => "Duplicate Content";
    public Version Version => new(1, 0, 0);
    public string Description => "Exact and near-duplicate content detection using SHA-256 and SimHash algorithms, plus domain/protocol variant validation";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new DuplicateContentTask(
            context.CreateLogger(nameof(DuplicateContentTask)),
            context.GetRepositoryAccessor()));
    }
}

