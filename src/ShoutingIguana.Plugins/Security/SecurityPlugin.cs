using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Plugins.Security;

[Plugin(Id = "com.shoutingiguana.security", Name = "Security & HTTPS", MinSdkVersion = "1.0.0")]
public class SecurityPlugin : IPlugin
{
    public string Id => "com.shoutingiguana.security";
    public string Name => "Security & HTTPS";
    public Version Version => new(1, 0, 0);
    public string Description => "Validates HTTPS implementation, detects mixed content, and checks security headers";

    public void Initialize(IHostContext context)
    {
        context.RegisterTask(new SecurityTask(
            context.CreateLogger(nameof(SecurityTask))));
    }
}

