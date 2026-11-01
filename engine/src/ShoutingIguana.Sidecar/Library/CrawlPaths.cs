namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// Which paths the UI may give, and the one spelling of a path that ids are made from. On Windows a path to another
/// computer is refused before anything touches it: opening one authenticates to that computer, which a page that took
/// over the webview could point at its own server. A mapped drive letter is the user's own choice, and stays allowed.
/// </summary>
internal static class CrawlPaths
{
    /// <summary>Links followed before giving up, as a loop of links would never end.</summary>
    private const int MAX_LINKS = 32;

    private static readonly char[] SEPARATORS = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Windows: a UNC or device path (<c>\\server\share</c>, <c>\\?\C:\</c>, <c>\\.\pipe</c>): two separators first.</summary>
    public static bool IsUncOrDevicePath(string path) =>
        OperatingSystem.IsWindows() && path.Length >= 2 && SEPARATORS.Contains(path[0]) && SEPARATORS.Contains(path[1]);

    /// <summary>Windows: a path on another computer, <c>\\server\share</c> or <c>\\?\UNC\server\share</c>.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (!IsUncOrDevicePath(path))
        {
            return false;
        }

        var device = path.Length >= 4 && path[2] is '?' or '.' && SEPARATORS.Contains(path[3]);
        return !device || path[4..].StartsWith("UNC", StringComparison.OrdinalIgnoreCase) && path.Length > 7 && SEPARATORS.Contains(path[7]);
    }

    /// <summary>
    /// The full path with every link along it followed (symbolic links, and junctions on Windows), so one file reached two
    /// ways has one id. From where the path stops existing, the rest is kept as given. A link to another computer isn't
    /// followed any further: the path to it is returned, for <see cref="IsNetworkPath"/> to refuse.
    /// </summary>
    public static string Resolve(string path)
    {
        var resolved = Path.GetFullPath(path);
        for (var links = 0; links < MAX_LINKS && !IsNetworkPath(resolved) && FirstLink(resolved) is { } link; links++)
        {
            resolved = WithoutDevicePrefix(Path.GetFullPath(Path.Join(link.Target, resolved[link.Path.Length..].TrimStart(SEPARATORS))));
        }

        return resolved;
    }

    /// <summary>Windows: <c>\\?\C:\crawls</c>, as a junction may give its target, is <c>C:\crawls</c>.</summary>
    private static string WithoutDevicePrefix(string path) =>
        OperatingSystem.IsWindows() && path.Length >= 7 && path.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(path[4]) &&
        path[5] == ':' && SEPARATORS.Contains(path[6])
            ? path[4..]
            : path;

    /// <summary>The first link along a full path, nearest the root, and the full path it points to.</summary>
    private static (string Path, string Target)? FirstLink(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        foreach (var part in fullPath[root.Length..].Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Join(current, part);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            // Cloud placeholders and other reparse points that aren't links have no target.
            if (attributes.HasFlag(FileAttributes.ReparsePoint) &&
                ((FileSystemInfo)(attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(current) : new FileInfo(current))).LinkTarget is { } target)
            {
                return (current, Path.GetFullPath(target, Path.GetDirectoryName(current) ?? root));
            }
        }

        return null;
    }
}
