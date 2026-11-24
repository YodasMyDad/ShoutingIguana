using System.Globalization;
using System.Runtime.InteropServices;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Which Chromium build Playwright downloads on a Linux it doesn't know. Playwright 1.56 names its builds by Ubuntu
/// release up to 24.04. On a later Ubuntu (26.04), or a distribution that keeps Ubuntu's versions (Pop!_OS, KDE neon,
/// TUXEDO OS), it asks for a build named after that release, which doesn't exist, and the install ends at once with
/// "Playwright does not support chromium-headless-shell on ubuntu26.04-x64". The 24.04 build runs there, so Playwright
/// is told to use it, through the variable it reads for this. Every other system is left to Playwright.
/// </summary>
internal static class BrowserHostPlatform
{
    public const string OVERRIDE_VARIABLE = "PLAYWRIGHT_HOST_PLATFORM_OVERRIDE";

    private const string OS_RELEASE_FILE = "/etc/os-release";

    /// <summary>The first Ubuntu release Playwright 1.56 has no build for. Move it on with the Playwright package.</summary>
    private const int FIRST_UBUNTU_WITHOUT_A_BUILD = 26;

    private const string NEWEST_UBUNTU_BUILD = "ubuntu24.04";

    /// <summary>Ubuntu, and the distributions Playwright reads as Ubuntu because they keep its version numbers.</summary>
    private static readonly string[] UBUNTU_VERSIONED = ["ubuntu", "pop", "neon", "tuxedo"];

    /// <summary>
    /// Before Playwright starts: its driver, and the installer it runs, inherit this process's environment. A value
    /// already set (by the user, or a test) is kept.
    /// </summary>
    public static void UseKnownBuild()
    {
        if (!OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OVERRIDE_VARIABLE)))
        {
            return;
        }

        string osRelease;
        try
        {
            osRelease = File.ReadAllText(OS_RELEASE_FILE);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (OverrideFor(osRelease, RuntimeInformation.ProcessArchitecture) is { } known)
        {
            Environment.SetEnvironmentVariable(OVERRIDE_VARIABLE, known);
        }
    }

    /// <summary>The build to name for this /etc/os-release, or null when Playwright's own choice stands.</summary>
    internal static string? OverrideFor(string osRelease, Architecture architecture)
    {
        var suffix = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (suffix is null)
        {
            return null;
        }

        var fields = osRelease
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim('"', '\''), StringComparer.Ordinal);
        if (!fields.TryGetValue("ID", out var id) || !UBUNTU_VERSIONED.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var major = fields.TryGetValue("VERSION_ID", out var version) ? version.Split('.')[0] : string.Empty;
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var release) &&
               release >= FIRST_UBUNTU_WITHOUT_A_BUILD
            ? $"{NEWEST_UBUNTU_BUILD}-{suffix}"
            : null;
    }
}
