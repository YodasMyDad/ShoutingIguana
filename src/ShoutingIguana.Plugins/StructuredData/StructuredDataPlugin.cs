using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.StructuredData;

[Plugin(Id = "com.shoutingiguana.structureddata", Name = "Structured Data", MinSdkVersion = "1.0.0")]
public class StructuredDataPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.structureddata";
    public string Name => "Structured Data";
    public Version Version => new(1, 0, 0);
    public string Description => "JSON-LD, Microdata, and Schema.org structured data extraction and validation";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new StructuredDataTask(context.CreateLogger(nameof(StructuredDataTask))));
    }
}

