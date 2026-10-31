using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Configuration;

public static class UserAgents
{
    public const string ROBOTS_TOKEN = "ShoutingIguana";

    private const string CHROME_VERSION = "141.0.0.0";
    private const string IDENTITY = "(compatible; ShoutingIguana/2.0; +https://github.com/YodasMyDad/ShoutingIguana)";

    public static ResolvedUserAgent Resolve(UserAgentPreset preset, string? customUserAgent) => preset switch
    {
        UserAgentPreset.ShoutingIguana => new(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROME_VERSION} Safari/537.36 {IDENTITY}",
            ROBOTS_TOKEN),
        UserAgentPreset.ChromeDesktop => new(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROME_VERSION} Safari/537.36",
            ROBOTS_TOKEN),
        UserAgentPreset.ChromeMobile => new(
            $"Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROME_VERSION} Mobile Safari/537.36",
            ROBOTS_TOKEN),
        UserAgentPreset.GooglebotSmartphone => new(
            $"Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROME_VERSION} Mobile Safari/537.36 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
            "Googlebot"),
        UserAgentPreset.GooglebotDesktop => new(
            $"Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; Googlebot/2.1; +http://www.google.com/bot.html) Chrome/{CHROME_VERSION} Safari/537.36",
            "Googlebot"),
        UserAgentPreset.Bingbot => new(
            $"Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm) Chrome/{CHROME_VERSION} Safari/537.36",
            "bingbot"),
        UserAgentPreset.Custom => new(customUserAgent ?? string.Empty, ROBOTS_TOKEN),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };
}
