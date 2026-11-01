using System.Security.Cryptography;
using System.Text;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// A crawl's id: 16 hex characters of the SHA-256 of its file's full path, case-folded where the file system ignores
/// case (Windows and macOS). Stable while the file stays where it is.
/// </summary>
internal static class CrawlIds
{
    public static readonly StringComparer PATHS = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string For(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var folded = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? fullPath.ToLowerInvariant() : fullPath;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folded)).AsSpan(0, 8));
    }

    /// <summary>What a crawl id looks like, checked before an id is looked up.</summary>
    public static bool IsWellFormed(string id) => id.Length == 16 && id.All(char.IsAsciiHexDigitLower);
}
