using Microsoft.Extensions.Logging;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.ImageAudit;

[Plugin(Id = "com.shoutingiguana.imageaudit", Name = "Image Audit", MinSdkVersion = "1.0.0")]
public class ImageAuditPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.imageaudit";
    public string Name => "Image Audit";
    public Version Version => new(1, 0, 0);
    public string Description => "Comprehensive image optimization, accessibility, and performance analysis";

    public void Initialize(IHostContext context)
    {
        var logger = context.CreateLogger<ImageAuditTask>();
        context.RegisterTask(new ImageAuditTask(logger));
    }
}

