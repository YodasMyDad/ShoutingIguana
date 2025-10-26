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
        context.RegisterTask(new CustomExtractionTask(
            context.CreateLogger(nameof(CustomExtractionTask)),
            context.GetServiceProvider()));
    }
}

