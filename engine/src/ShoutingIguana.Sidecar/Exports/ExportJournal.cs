using System.Text.Json;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Exports;

/// <summary>
/// The sidecar's note of the files its exports are part-way through writing, kept in its own state folder. An export
/// writes beside its target under another name and moves the file into place once whole; one that is cancelled or
/// fails deletes what it wrote. A sidecar that is killed (the shell ends it with its job object, or it crashes) can do
/// neither, and its part file would stay in the user's folder for good.
/// <para>
/// So each part file's full path is written here before the file is created, and taken out once the file has gone. A
/// sidecar that starts reads the note its predecessor left (<see cref="Recover"/>), deletes each of those files that is
/// still there, and starts with an empty note. Only files named here are ever deleted: the user's folder is never
/// searched for files that look like one of these. One state folder has one sidecar (its lock), so what the note holds
/// at a start is never an export still running.
/// </para>
/// </summary>
internal sealed partial class ExportJournal(SidecarOptions options, ILogger<ExportJournal> logger) : IExportJournal
{
    /// <summary>The note's file, in the state folder.</summary>
    public const string FILE_NAME = "exports-in-progress.json";

    private readonly Lock _sync = new();
    private readonly string _path = Path.Combine(options.StateDirectory, FILE_NAME);
    private readonly HashSet<string> _parts = new(StringComparer.Ordinal);

    /// <summary>The part files noted now, for tests.</summary>
    internal IReadOnlyList<string> Parts
    {
        get
        {
            lock (_sync)
            {
                return [.. _parts];
            }
        }
    }

    public void Opening(string partFile)
    {
        lock (_sync)
        {
            if (_parts.Add(partFile))
            {
                Save();
            }
        }
    }

    public void Closed(string partFile)
    {
        lock (_sync)
        {
            if (_parts.Remove(partFile))
            {
                Save();
            }
        }
    }

    /// <summary>
    /// At the sidecar's start: deletes each part file the note names that is still there, and empties the note. A file
    /// that can't be deleted is left where it is, and logged. A name in the note that isn't a part file's (the note was
    /// changed by hand, or damaged) is never deleted.
    /// </summary>
    public void Recover()
    {
        lock (_sync)
        {
            foreach (var partFile in Read())
            {
                if (!IsPartFile(partFile))
                {
                    logger.LogWarning("The note of exports in progress names {Path}, which isn't a part file of an export: left alone", partFile);
                    continue;
                }

                try
                {
                    if (File.Exists(partFile))
                    {
                        File.Delete(partFile);
                        logger.LogInformation("Deleted {Path}, the part file of an export that an earlier run never finished", partFile);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    logger.LogWarning(exception, "The part file {Path} of an export that an earlier run never finished couldn't be deleted: it is left there", partFile);
                }
            }

            _parts.Clear();
            Save();
        }
    }

    /// <summary>
    /// Whether a path is one an export's part file has: a full path on this computer whose name is a dot, the target's
    /// name, 32 hex digits and <c>.tmp</c>, with a word after it for a file set aside beside it (<c>.strings</c>).
    /// </summary>
    internal static bool IsPartFile(string path) =>
        Path.IsPathFullyQualified(path) && !CrawlPaths.IsUncOrDevicePath(path) && PartFileName().IsMatch(Path.GetFileName(path));

    [GeneratedRegex(@"^\..+\.[0-9a-f]{32}\.tmp(\.[a-z]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PartFileName();

    /// <summary>The paths the note holds; none when there is no note, or it can't be read as one.</summary>
    private List<string> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!)]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(exception, "The note of exports in progress at {Path} couldn't be read: nothing it named is deleted", _path);
            return [];
        }
    }

    /// <summary>
    /// Writes the note whole, beside itself and then into place, so it is never half a note. Under the lock. It mustn't
    /// throw: a note that can't be kept costs a part file left after a crash, never the export.
    /// </summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(options.StateDirectory);
            var beside = _path + ".new";
            using (var file = File.Create(beside))
            using (var json = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartArray();
                foreach (var partFile in _parts.Order(StringComparer.Ordinal))
                {
                    json.WriteStringValue(partFile);
                }

                json.WriteEndArray();
            }

            File.Move(beside, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogWarning(exception, "The note of exports in progress couldn't be written to {Path}", _path);
        }
    }
}
