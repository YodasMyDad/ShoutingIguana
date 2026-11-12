using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Options;

/// <summary>
/// The limits every analysis here runs with, which the user can change: the engine's, with the user's own over them, kept
/// in <c>analysis-settings.json</c> in the state folder. Only the limits that differ from the engine's are written, so a
/// limit the user never changed follows the engine's default when a later version moves it.
/// <list type="bullet">
/// <item><see cref="Current"/> is one immutable set: an analysis takes it as it starts and keeps it to its end, whatever
/// is saved meanwhile.</item>
/// <item>An analysis that ran with other limits than the current ones is out of date (the engine compares the limits it
/// stored), so a change shows on every analysed crawl from the next time it's listed or asked for.</item>
/// </list>
/// </summary>
internal sealed class AnalysisSettingsStore
{
    private const string FILE_NAME = "analysis-settings.json";

    private readonly Lock _sync = new();
    private readonly string _file;
    private readonly ILogger<AnalysisSettingsStore> _logger;
    private volatile AnalysisSettings _current;

    public AnalysisSettingsStore(SidecarOptions options, ILogger<AnalysisSettingsStore> logger)
    {
        _file = Path.Combine(options.StateDirectory, FILE_NAME);
        _logger = logger;
        _current = Load();
    }

    /// <summary>The limits in force.</summary>
    public AnalysisSettings Current => _current;

    /// <summary>What's wrong with these values, by limit; none when they could be saved.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Validate(IReadOnlyDictionary<string, JsonElement> requested)
    {
        Parse(requested, out var errors);
        return errors;
    }

    /// <summary>Saves these limits as the ones in force: a limit left out, or null, takes the engine's value.</summary>
    /// <exception cref="ProblemException">invalid-settings: a value is out of its range or isn't a whole number; nothing is saved.</exception>
    public AnalysisSettings Save(IReadOnlyDictionary<string, JsonElement> requested)
    {
        var settings = Parse(requested, out var errors);
        if (errors.Count > 0)
        {
            throw Problems.InvalidSettings(errors);
        }

        return Store(settings);
    }

    /// <summary>Puts these limits back to the engine's values, the others as they are; every limit when none is named.</summary>
    /// <exception cref="ProblemException">invalid-settings: there's no limit of a name given; nothing is changed.</exception>
    public AnalysisSettings Reset(IReadOnlyList<string?>? limits)
    {
        if (limits is null or { Count: 0 })
        {
            return Store(AnalysisLimits.Defaults);
        }

        Dictionary<string, IReadOnlyList<string>> errors = new(StringComparer.Ordinal);
        for (var index = 0; index < limits.Count; index++)
        {
            if (limits[index] is not { } name || AnalysisLimits.Find(name) is null)
            {
                errors[$"limits[{index}]"] = [$"There's no limit called '{limits[index]}': there are {string.Join(", ", AnalysisLimits.All.Select(limit => limit.Id))}."];
            }
        }

        if (errors.Count > 0)
        {
            throw Problems.InvalidSettings(errors);
        }

        lock (_sync)
        {
            // What's left always passes: taking a limit back to the engine's can only break a pair (a longest length under
            // its shortest), and then the other of the pair goes back with it.
            var kept = AnalysisLimits.Values(_current).Where(value => !limits.Contains(value.Key)).ToDictionary(StringComparer.Ordinal);
            var settings = AnalysisLimits.Apply(kept, out var broken);
            foreach (var field in broken.Keys)
            {
                kept.Remove(field);
                kept.Remove(AnalysisLimits.PartnerOf(field) ?? field);
            }

            return Store(broken.Count == 0 ? settings : AnalysisLimits.Apply(kept, out _));
        }
    }

    private AnalysisSettings Store(AnalysisSettings settings)
    {
        lock (_sync)
        {
            var changed = AnalysisLimits.Values(settings).Where(value => value.Value != AnalysisLimits.Find(value.Key)!.Get(AnalysisLimits.Defaults))
                .ToDictionary(StringComparer.Ordinal);
            try
            {
                StateFile.Write(_file, JsonSerializer.Serialize(changed, SidecarJsonContext.Default.DictionaryStringInt64));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Problems.FileError("The analysis's limits couldn't be saved.", exception.Message);
            }

            _current = settings;
            return settings;
        }
    }

    /// <summary>The values asked for as limits, each checked by itself: a whole number, within its range.</summary>
    private static AnalysisSettings Parse(IReadOnlyDictionary<string, JsonElement> requested, out Dictionary<string, IReadOnlyList<string>> errors)
    {
        Dictionary<string, long> values = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> wrong = new(StringComparer.Ordinal);
        foreach (var (name, element) in requested)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
            {
                values[name] = value;
            }
            else
            {
                wrong[name] = ["This must be a whole number."];
            }
        }

        var settings = AnalysisLimits.Apply(values, out errors);
        foreach (var (name, messages) in wrong)
        {
            errors[name] = messages;
        }

        return settings;
    }

    /// <summary>The saved limits over the engine's. A file that can't be read, or a value in it that couldn't be saved today, is passed over.</summary>
    private AnalysisSettings Load()
    {
        if (!File.Exists(_file))
        {
            return AnalysisLimits.Defaults;
        }

        try
        {
            var saved = JsonSerializer.Deserialize(File.ReadAllText(_file), SidecarJsonContext.Default.DictionaryStringJsonElement) ?? [];
            Dictionary<string, JsonElement> known = saved.Where(value => AnalysisLimits.Find(value.Key) is not null).ToDictionary(StringComparer.Ordinal);
            var settings = Parse(known, out var errors);
            if (errors.Count == 0)
            {
                return settings;
            }

            // Edited by hand, or saved by a version with other ranges: the values that still stand are kept.
            _logger.LogWarning("Some limits in {SettingsFile} can't be used, and take the engine's values: {Limits}", _file, string.Join(", ", errors.Keys));
            foreach (var field in errors.Keys)
            {
                known.Remove(field);
            }

            settings = Parse(known, out var still);
            return still.Count == 0 ? settings : AnalysisLimits.Defaults;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read {SettingsFile}; using the engine's limits", _file);
            return AnalysisLimits.Defaults;
        }
    }
}
