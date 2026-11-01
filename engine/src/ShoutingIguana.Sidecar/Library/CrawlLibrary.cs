using System.Text.Json;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Library.Models;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// Which crawls the library lists: the crawl files at the top of the data folder, plus files opened from elsewhere,
/// minus those removed from the library (not from disk). What it adds and removes is kept in <c>library.json</c> in
/// the state folder; the crawl files themselves are never written here.
/// </summary>
internal sealed class CrawlLibrary
{
    private const string FILE_NAME = "library.json";
    private const string EXTENSION = ".sicrawl";

    private static readonly EnumerationOptions TOP_LEVEL_CRAWLS = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
        RecurseSubdirectories = false,
        IgnoreInaccessible = true
    };

    private readonly Lock _sync = new();
    private readonly string _dataDirectory;
    private readonly string _file;
    private readonly ILogger<CrawlLibrary> _logger;
    private readonly Dictionary<string, OpenedCrawl> _opened = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _removed = new(StringComparer.Ordinal);

    // Every crawl path seen, by id, removed ones included: they can still be restored or deleted.
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);

    public CrawlLibrary(SidecarOptions options, ILogger<CrawlLibrary> logger)
    {
        _dataDirectory = Path.TrimEndingDirectorySeparator(options.DataDirectory);
        _file = Path.Combine(options.StateDirectory, FILE_NAME);
        _logger = logger;
        Load();
    }

    public static bool IsCrawlFileName(string path) => path.EndsWith(EXTENSION, StringComparison.OrdinalIgnoreCase);

    /// <summary>The crawls in the library, by id: the data folder's first, then those opened from elsewhere.</summary>
    public IReadOnlyList<(string Id, string Path)> List()
    {
        var scanned = Scan();
        lock (_sync)
        {
            HashSet<string> listed = new(StringComparer.Ordinal);
            List<(string Id, string Path)> crawls = [];
            foreach (var path in scanned.Concat(_opened.Values.Select(opened => opened.Path)))
            {
                var id = CrawlIds.For(path);
                _known.TryAdd(id, path);
                if (!_removed.ContainsKey(id) && listed.Add(id))
                {
                    crawls.Add((id, path));
                }
            }

            return crawls;
        }
    }

    /// <summary>The file of any crawl the library has seen, removed ones included. Looks at the data folder again for one it hasn't.</summary>
    public string? Find(string id)
    {
        lock (_sync)
        {
            if (_known.TryGetValue(id, out var path))
            {
                return path;
            }
        }

        List();
        lock (_sync)
        {
            return _known.GetValueOrDefault(id);
        }
    }

    public bool Contains(string id)
    {
        lock (_sync)
        {
            return _known.ContainsKey(id) && !_removed.ContainsKey(id);
        }
    }

    /// <summary>The start URL a file opened from elsewhere had, for when it has gone missing.</summary>
    public string? OpenedStartUrl(string id)
    {
        lock (_sync)
        {
            return _opened.GetValueOrDefault(id)?.StartUrl;
        }
    }

    /// <summary>A crawl created in the data folder, which the next look at the folder would find anyway.</summary>
    public void Created(string id, string path)
    {
        lock (_sync)
        {
            _known[id] = path;
        }
    }

    /// <summary>Adds a crawl file the user opened, or brings one back that was removed.</summary>
    public string Add(string path, string startUrl)
    {
        var id = CrawlIds.For(path);
        lock (_sync)
        {
            _known[id] = path;
            var changed = _removed.Remove(id);
            if (!IsInDataFolder(path))
            {
                _opened[id] = new OpenedCrawl(path, startUrl);
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }

        return id;
    }

    /// <summary>Takes a crawl out of the library. The file stays where it is, and <see cref="Restore"/> brings it back.</summary>
    public void Remove(string id)
    {
        lock (_sync)
        {
            if (_known.TryGetValue(id, out var path) && _removed.TryAdd(id, path))
            {
                Save();
            }
        }
    }

    public void Restore(string id)
    {
        lock (_sync)
        {
            if (_removed.Remove(id))
            {
                Save();
            }
        }
    }

    /// <summary>Forgets a crawl whose file was deleted.</summary>
    public void Forget(string id)
    {
        lock (_sync)
        {
            _known.Remove(id);
            if (_opened.Remove(id) | _removed.Remove(id))
            {
                Save();
            }
        }
    }

    private bool IsInDataFolder(string path) =>
        CrawlIds.PATHS.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? string.Empty), _dataDirectory);

    private List<string> Scan()
    {
        try
        {
            return Directory.Exists(_dataDirectory) ? [.. Directory.EnumerateFiles(_dataDirectory, "*" + EXTENSION, TOP_LEVEL_CRAWLS)] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not list the crawls in {DataDirectory}", _dataDirectory);
            return [];
        }
    }

    private void Load()
    {
        if (!File.Exists(_file))
        {
            return;
        }

        try
        {
            var library = JsonSerializer.Deserialize(File.ReadAllText(_file), SidecarJsonContext.Default.LibraryFile);
            foreach (var opened in library?.Opened ?? [])
            {
                var id = CrawlIds.For(opened.Path);
                _opened[id] = opened;
                _known[id] = opened.Path;
            }

            foreach (var path in library?.Removed ?? [])
            {
                var id = CrawlIds.For(path);
                _removed[id] = path;
                _known.TryAdd(id, path);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The crawls themselves are untouched: the library starts again from the data folder.
            _logger.LogWarning(exception, "Could not read {LibraryFile}; starting with the data folder's crawls only", _file);
        }
    }

    /// <summary>Written beside the file and moved over it, so a crash never leaves half a library.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new LibraryFile([.. _opened.Values], [.. _removed.Values]),
                SidecarJsonContext.Default.LibraryFile));
            File.Move(temporary, _file, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Problems.FileError("The library couldn't be saved.", exception.Message);
        }
    }
}
