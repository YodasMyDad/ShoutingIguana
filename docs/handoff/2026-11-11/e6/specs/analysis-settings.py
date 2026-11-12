S = 'src/ShoutingIguana.Sidecar/'

WRITES = [
(S + 'Options/Models/AnalysisLimit.cs', '''using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Options.Models;

/// <summary>One of the analysis's limits, as Settings shows it: what it is, in what unit, and what it may be set to.</summary>
/// <param name="Id">The JSON name of the limit's property in <see cref="AnalysisSettings"/>, which the API and the saved file key it by.</param>
/// <param name="Unit"><c>characters</c>, <c>words</c>, <c>bits</c>, <c>bytes</c> or <c>ms</c>.</param>
/// <param name="Min">The least it may be set to.</param>
/// <param name="Max">The most it may be set to.</param>
/// <param name="Description">What the limit does, in one sentence the UI shows as it is.</param>
internal sealed record AnalysisLimit(
    string Id,
    string Unit,
    long Min,
    long Max,
    string Description,
    Func<AnalysisSettings, long> Get,
    Func<AnalysisSettings, long, AnalysisSettings> Set);
'''),
(S + 'Options/AnalysisLimits.cs', '''using System.Globalization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Options.Models;

namespace ShoutingIguana.Sidecar.Options;

/// <summary>
/// Every limit the analysis judges by (<see cref="AnalysisSettings"/>), described for Settings: its unit, the range it may
/// be set within and what it does, with the engine's value as its default. The engine's own check of a set of limits
/// (<see cref="AnalysisSettings.Validate"/>) says only that something is wrong; here each limit is checked by itself, so
/// a message is about one field.
/// </summary>
internal static class AnalysisLimits
{
    private const string CHARACTERS = "characters";
    private const string WORDS = "words";
    private const string BYTES = "bytes";

    /// <summary>The longest a length in characters may be set to: far past any title, description, alt text or URL worth judging.</summary>
    private const long MAX_CHARACTERS = 10_000;

    private const long MAX_WORDS = 1_000_000;

    /// <summary>The largest a size may be set to: 10 GB.</summary>
    private const long MAX_BYTES = 10L * 1024 * 1024 * 1024;

    /// <summary>The engine's limits: each limit's default.</summary>
    public static AnalysisSettings Defaults { get; } = new();

    /// <summary>Every limit, in the order Settings lists them.</summary>
    public static IReadOnlyList<AnalysisLimit> All { get; } =
    [
        new("titleMinLength", CHARACTERS, 0, MAX_CHARACTERS, "A title shorter than this is flagged as too short.",
            settings => settings.TitleMinLength, (settings, value) => settings with { TitleMinLength = (int)value }),
        new("titleMaxLength", CHARACTERS, 1, MAX_CHARACTERS, "A title longer than this is flagged as too long: search results cut it short.",
            settings => settings.TitleMaxLength, (settings, value) => settings with { TitleMaxLength = (int)value }),
        new("descriptionMinLength", CHARACTERS, 0, MAX_CHARACTERS, "A meta description shorter than this is flagged as too short.",
            settings => settings.DescriptionMinLength, (settings, value) => settings with { DescriptionMinLength = (int)value }),
        new("descriptionMaxLength", CHARACTERS, 1, MAX_CHARACTERS, "A meta description longer than this is flagged as too long: search results cut it short.",
            settings => settings.DescriptionMaxLength, (settings, value) => settings with { DescriptionMaxLength = (int)value }),
        new("minWordCount", WORDS, 0, MAX_WORDS, "A page with fewer words of main content than this is flagged as thin.",
            settings => settings.MinWordCount, (settings, value) => settings with { MinWordCount = (int)value }),
        new("duplicateMinWordCount", WORDS, 0, MAX_WORDS, "A page with fewer words of main content than this is left out of the duplicate content checks.",
            settings => settings.DuplicateMinWordCount, (settings, value) => settings with { DuplicateMinWordCount = (int)value }),
        new("nearDuplicateMaxBits", "bits", 0, 32,
            "How far apart two pages' content fingerprints may be, of 256 bits, for the pages to count as near duplicates: 24 is about 96% the same, and fewer is stricter.",
            settings => settings.NearDuplicateMaxBits, (settings, value) => settings with { NearDuplicateMaxBits = (int)value }),
        new("imageAltMaxLength", CHARACTERS, 1, MAX_CHARACTERS, "An image's alt text longer than this is flagged as too long.",
            settings => settings.ImageAltMaxLength, (settings, value) => settings with { ImageAltMaxLength = (int)value }),
        new("largeImageBytes", BYTES, 1, MAX_BYTES, "An image larger than this is flagged as too large.",
            settings => settings.LargeImageBytes, (settings, value) => settings with { LargeImageBytes = value }),
        new("largePageBytes", BYTES, 1, MAX_BYTES, "A page whose HTML is larger than this is flagged as too large.",
            settings => settings.LargePageBytes, (settings, value) => settings with { LargePageBytes = value }),
        new("slowResponseMs", "ms", 1, 3_600_000, "A page that takes longer than this to answer is flagged as slow.",
            settings => settings.SlowResponseMs, (settings, value) => settings with { SlowResponseMs = (int)value }),
        new("maxUrlLength", CHARACTERS, 1, MAX_CHARACTERS, "A URL longer than this is flagged as too long.",
            settings => settings.MaxUrlLength, (settings, value) => settings with { MaxUrlLength = (int)value })
    ];

    /// <summary>A minimum and the maximum that must be more than it, with what they're the lengths of.</summary>
    private static readonly (string Min, string Max, string What)[] PAIRS =
    [
        ("titleMinLength", "titleMaxLength", "title"),
        ("descriptionMinLength", "descriptionMaxLength", "meta description")
    ];

    public static AnalysisLimit? Find(string id) => All.FirstOrDefault(limit => limit.Id == id);

    /// <summary>The settings' value of every limit, by its id, in the limits' order.</summary>
    public static Dictionary<string, long> Values(AnalysisSettings settings) => All.ToDictionary(limit => limit.Id, limit => limit.Get(settings), StringComparer.Ordinal);

    /// <summary>
    /// The engine's limits with these values in place of its own, and what's wrong with any of them, by the limit: one out of
    /// its range, a longest length that isn't more than its shortest. A limit not named keeps the engine's value.
    /// </summary>
    public static AnalysisSettings Apply(IReadOnlyDictionary<string, long> values, out Dictionary<string, IReadOnlyList<string>> errors)
    {
        errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var settings = Defaults;
        foreach (var limit in All)
        {
            if (!values.TryGetValue(limit.Id, out var value))
            {
                continue;
            }

            if (value < limit.Min || value > limit.Max)
            {
                errors[limit.Id] = [string.Create(CultureInfo.InvariantCulture, $"This must be from {limit.Min:N0} to {limit.Max:N0} {limit.Unit}.")];
            }
            else
            {
                settings = limit.Set(settings, value);
            }
        }

        foreach (var (min, max, what) in PAIRS)
        {
            var shortest = Find(min)!.Get(settings);
            if (!errors.ContainsKey(min) && !errors.ContainsKey(max) && Find(max)!.Get(settings) <= shortest)
            {
                errors[max] = [string.Create(CultureInfo.InvariantCulture, $"The longest {what} allowed must be more than the shortest ({shortest:N0}).")];
            }
        }

        // The engine has the last word: a rule it has that isn't one of those above is still a refusal, for the limits as a whole.
        if (errors.Count == 0 && settings.Validate() is { Count: > 0 } refused)
        {
            errors[SETTINGS] = refused;
        }

        return settings;
    }

    /// <summary>What a refusal that is about no one limit is keyed by.</summary>
    public const string SETTINGS = "settings";
}
'''),
(S + 'Options/AnalysisSettingsStore.cs', '''using System.Text.Json;
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
'''),
(S + 'Api/Models/AnalysisLimitDto.cs', '''namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One of the analysis's limits, for Settings.</summary>
/// <param name="Id">The limit's name, which the settings are keyed by.</param>
/// <param name="Value">What it's set to now.</param>
/// <param name="Default">The engine's value, which resetting it puts back.</param>
/// <param name="Unit"><c>characters</c>, <c>words</c>, <c>bits</c>, <c>bytes</c> or <c>ms</c>.</param>
/// <param name="Min">The least it may be set to.</param>
/// <param name="Max">The most it may be set to.</param>
/// <param name="Description">What it does, in a sentence to show as it is.</param>
internal sealed record AnalysisLimitDto(string Id, long Value, long Default, string Unit, long Min, long Max, string Description);
'''),
(S + 'Api/Models/AnalysisSettingsDto.cs', '''namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The limits every analysis runs with, and what each is.</summary>
/// <param name="Settings">Each limit's value by its name: the body a save takes.</param>
/// <param name="Limits">Every limit with its value, the engine's default, its unit, its range and what it does.</param>
/// <param name="IsDefault">Every limit is at the engine's value.</param>
internal sealed record AnalysisSettingsDto(IReadOnlyDictionary<string, long> Settings, IReadOnlyList<AnalysisLimitDto> Limits, bool IsDefault);
'''),
(S + 'Api/Models/ResetAnalysisSettingsRequest.cs', '''using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Limits">The limits to put back to the engine's values; all of them when left out or empty.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ResetAnalysisSettingsRequest(IReadOnlyList<string?>? Limits);
'''),
(S + 'Api/AnalysisSettingsEndpoints.cs', '''using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// The analysis's limits, for Settings: what's in force with what each limit is, saving new ones, checking them as
/// they're typed, and putting one or all back to the engine's.
/// </summary>
internal static class AnalysisSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/analysis-settings", (AnalysisSettingsStore settings) => TypedResults.Json(Dto(settings.Current), SidecarJsonContext.Default.AnalysisSettingsDto));

        api.MapPut("/api/analysis-settings", async (HttpRequest request, AnalysisSettingsStore settings) =>
            TypedResults.Json(Dto(settings.Save(await ReadAsync(request).ConfigureAwait(false))), SidecarJsonContext.Default.AnalysisSettingsDto));

        // 200 with the messages, keyed by limit. A body that isn't a JSON object, or a limit that doesn't exist, is a bad
        // request (400), as on every other call.
        api.MapPost("/api/analysis-settings/validate", async (HttpRequest request) =>
            TypedResults.Json(new ValidationDto(AnalysisSettingsStore.Validate(await ReadAsync(request).ConfigureAwait(false))), SidecarJsonContext.Default.ValidationDto));

        api.MapPost("/api/analysis-settings/reset", async (HttpRequest request, AnalysisSettingsStore settings) =>
        {
            var reset = await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.ResetAnalysisSettingsRequest).ConfigureAwait(false);
            return TypedResults.Json(Dto(settings.Reset(reset?.Limits)), SidecarJsonContext.Default.AnalysisSettingsDto);
        });
    }

    /// <summary>The limits sent, by name: a name that's no limit's is a property the API doesn't know.</summary>
    private static async Task<Dictionary<string, JsonElement>> ReadAsync(HttpRequest request)
    {
        var values = await RequestBody.ReadAsync(request, SidecarJsonContext.Default.DictionaryStringJsonElement).ConfigureAwait(false);
        if (values.Keys.FirstOrDefault(name => AnalysisLimits.Find(name) is null) is { } unknown)
        {
            throw Problems.BadRequest("The request body isn't the JSON this call takes.",
                $"There's no limit called '{unknown}': there are {string.Join(", ", AnalysisLimits.All.Select(limit => limit.Id))}.");
        }

        return values;
    }

    private static AnalysisSettingsDto Dto(AnalysisSettings settings) => new(
        AnalysisLimits.Values(settings),
        [.. AnalysisLimits.All.Select(limit => new AnalysisLimitDto(limit.Id, limit.Get(settings), limit.Get(AnalysisLimits.Defaults), limit.Unit, limit.Min, limit.Max, limit.Description))],
        settings == AnalysisLimits.Defaults);
}
'''),
]

EDITS = [
(S + 'Errors/Problems.cs',
'''    public static ProblemException NeedsSecrets(IReadOnlyList<string> missingHeaders, bool proxyCredentials)
''',
'''    /// <summary>The analysis's limits can't be saved as sent, with what's wrong by limit.</summary>
    public static ProblemException InvalidSettings(IReadOnlyDictionary<string, IReadOnlyList<string>> errors) =>
        new(new ProblemDto("invalid-settings", "Some of the analysis's limits need changing.", 422) { Errors = errors });

    public static ProblemException NeedsSecrets(IReadOnlyList<string> missingHeaders, bool proxyCredentials)
'''),
(S + 'Crawls/CrawlManager.cs',
'''    EventHub events,
    AlertLog alerts,
    ILoggerFactory loggerFactory)
{
''',
'''    EventHub events,
    AlertLog alerts,
    AnalysisSettingsStore analysisSettings,
    ILoggerFactory loggerFactory)
{
'''),
(S + 'Crawls/CrawlManager.cs',
'''    /// <summary>
    /// The thresholds every analysis here runs with, and so what an analysis must have used to be up to date. The
    /// engine's defaults until Settings can change them.
    /// </summary>
    public AnalysisSettings AnalysisSettings { get; } = new();
''',
'''    /// <summary>
    /// The thresholds every analysis here runs with, and so what an analysis must have used to be up to date: the engine's,
    /// with what Settings has changed over them (<see cref="AnalysisSettingsStore"/>). Each read gives the set in force
    /// then, whole and unchanging: an analysis takes it once, as it starts.
    /// </summary>
    public AnalysisSettings AnalysisSettings => analysisSettings.Current;
'''),
(S + 'Crawls/CrawlManager.cs',
'''        var cancellation = crawl.Analysis!;
        string? error = null;
        string? errorType = null;
        try
        {
            var summary = await new CrawlAnalyzer(loggerFactory)
                .AnalyzeAsync(crawl.Path, AnalysisSettings, new SidecarAnalysisProgress(this, crawl), cancellation.Token).ConfigureAwait(false);
''',
'''        var cancellation = crawl.Analysis!;
        string? error = null;
        string? errorType = null;
        try
        {
            // The limits in force as it starts are the ones it runs with to its end, and stores: saved again meanwhile, the
            // analysis is out of date the moment it's done, and says so.
            var settings = AnalysisSettings;
            var summary = await new CrawlAnalyzer(loggerFactory)
                .AnalyzeAsync(crawl.Path, settings, new SidecarAnalysisProgress(this, crawl), cancellation.Token).ConfigureAwait(false);
'''),
(S + 'SidecarHost.cs',
'''        builder.Services.AddSingleton<DefaultsStore>();
''',
'''        builder.Services.AddSingleton<DefaultsStore>();
        builder.Services.AddSingleton<AnalysisSettingsStore>();
'''),
(S + 'SidecarHost.cs',
'''        OptionsEndpoints.Map(app);
''',
'''        OptionsEndpoints.Map(app);
        AnalysisSettingsEndpoints.Map(app);
'''),
(S + 'Serialization/SidecarJsonContext.cs',
'''[JsonSerializable(typeof(RobotsOriginsRequest))]
''',
'''[JsonSerializable(typeof(RobotsOriginsRequest))]
[JsonSerializable(typeof(AnalysisSettingsDto))]
[JsonSerializable(typeof(ResetAnalysisSettingsRequest))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
'''),
(S + 'Serialization/SidecarJsonContext.cs',
'''using System.Text.Json.Serialization;
''',
'''using System.Text.Json;
using System.Text.Json.Serialization;
'''),
]
