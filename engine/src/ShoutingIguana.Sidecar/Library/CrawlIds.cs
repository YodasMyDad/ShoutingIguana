using System.Security.Cryptography;
using System.Text;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// A crawl's id: 16 hex characters of the SHA-256 of its file's full path with links followed (<see cref="CrawlPaths.Resolve"/>),
/// so a file reached through a junction or a symbolic link has the id it has when reached directly. The path is
/// case-folded on Windows and macOS, whose file systems ignore case by default; the rule goes by the operating system,
/// not the volume, so on a folder set to tell case apart (rare) two names differing only in case would share an id.
/// Stable while the file stays where it is.
/// </summary>
internal static class CrawlIds
{
    public static readonly StringComparer PATHS = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string For(string path)
    {
        var resolved = CrawlPaths.Resolve(path);
        var folded = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? resolved.ToLowerInvariant() : resolved;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folded)).AsSpan(0, 8));
    }

    /// <summary>What a crawl id looks like, checked before an id is looked up.</summary>
    public static bool IsWellFormed(string id) => id.Length == 16 && id.All(char.IsAsciiHexDigitLower);
}
