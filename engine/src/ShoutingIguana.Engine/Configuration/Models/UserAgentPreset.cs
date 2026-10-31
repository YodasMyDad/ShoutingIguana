namespace ShoutingIguana.Engine.Configuration.Models;

public enum UserAgentPreset
{
    /// <summary>Chrome-compatible string that still identifies the crawler, so sites can allowlist it.</summary>
    ShoutingIguana,
    ChromeDesktop,
    ChromeMobile,
    GooglebotSmartphone,
    GooglebotDesktop,
    Bingbot,
    Custom
}
