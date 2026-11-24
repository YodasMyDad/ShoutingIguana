using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Fetching;

/// <summary>
/// Finds the Chrome, Edge or Chromium this computer has. A site's human check tells a person's browser from the
/// headless one the crawl downloads, so a host that one can't read is read in a browser a person would use. Chrome
/// first, as the commonest, then Edge, which every Windows has, then Chromium.
/// </summary>
internal static class InstalledBrowsers
{
    /// <summary>Names a browser's program file, for a computer that keeps it somewhere else. Empty looks in the usual places.</summary>
    public const string PATH_VARIABLE = "SHOUTINGIGUANA_BROWSER";

    /// <summary>What a browser named by <see cref="PATH_VARIABLE"/> is called: which one it is isn't known.</summary>
    public const string NAMED = "the browser";

    /// <summary>The browser's name as a sentence starts with it: "A Chrome", "An Edge".</summary>
    public static string WithArticle(string name) => (name.Length > 0 && "AEIOUaeiou".Contains(name[0]) ? "An " : "A ") + name;

    public static InstalledBrowser? Find()
    {
        if (Environment.GetEnvironmentVariable(PATH_VARIABLE) is { Length: > 0 } named && File.Exists(named))
        {
            return new InstalledBrowser(NAMED, named);
        }

        return Candidates().FirstOrDefault(candidate => File.Exists(candidate.Path));
    }

    /// <summary>
    /// Whether a window can be shown. A Linux without a display (a server, a container) can't show one, and a browser
    /// started there would only fail.
    /// </summary>
    public static bool HasDisplay() =>
        !OperatingSystem.IsLinux() ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    private static IEnumerable<InstalledBrowser> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] roots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            ];
            foreach (var root in roots.Where(root => root.Length > 0))
            {
                yield return new InstalledBrowser("Chrome", Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
            }

            foreach (var root in roots.Where(root => root.Length > 0))
            {
                yield return new InstalledBrowser("Edge", Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var applications in new[] { "/Applications", Path.Combine(home, "Applications") })
            {
                yield return new InstalledBrowser("Chrome", Path.Combine(applications, "Google Chrome.app", "Contents", "MacOS", "Google Chrome"));
            }

            foreach (var applications in new[] { "/Applications", Path.Combine(home, "Applications") })
            {
                yield return new InstalledBrowser("Edge", Path.Combine(applications, "Microsoft Edge.app", "Contents", "MacOS", "Microsoft Edge"));
            }

            yield return new InstalledBrowser("Chromium", "/Applications/Chromium.app/Contents/MacOS/Chromium");
            yield break;
        }

        (string Name, string File)[] programs =
        [
            ("Chrome", "google-chrome"), ("Chrome", "google-chrome-stable"), ("Edge", "microsoft-edge"),
            ("Edge", "microsoft-edge-stable"), ("Chromium", "chromium"), ("Chromium", "chromium-browser")
        ];
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/bin", "/usr/local/bin", "/snap/bin", "/opt/google/chrome"])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var (name, file) in programs)
        {
            foreach (var folder in folders)
            {
                yield return new InstalledBrowser(name, Path.Combine(folder, file));
            }
        }
    }
}
