using System.Globalization;
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
    private const string PIXELS = "pixels";
    private const string WORDS = "words";
    private const string BYTES = "bytes";

    /// <summary>The longest a length in characters may be set to: far past any title, description, alt text or URL worth judging.</summary>
    private const long MAX_CHARACTERS = 10_000;

    /// <summary>The widest a width in pixels may be set to: far past any title or description worth judging.</summary>
    private const long MAX_PIXELS = 100_000;

    private const long MAX_WORDS = 1_000_000;

    /// <summary>The largest a size may be set to: 10 GB.</summary>
    private const long MAX_BYTES = 10L * 1024 * 1024 * 1024;

    /// <summary>The engine's limits: each limit's default.</summary>
    public static AnalysisSettings Defaults { get; } = new();

    /// <summary>Every limit, in the order Settings lists them.</summary>
    public static IReadOnlyList<AnalysisLimit> All { get; } =
    [
        new("titleMinPixels", PIXELS, 0, MAX_PIXELS, "A title narrower than this is flagged as too short.",
            settings => settings.TitleMinPixels, (settings, value) => settings with { TitleMinPixels = (int)value }, AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX),
        new("titleMaxPixels", PIXELS, 1, MAX_PIXELS,
            "A title wider than this in search results is flagged as too long: Google's desktop results would likely cut it off. Measured in Arial at 20 px, as Google shows titles.",
            settings => settings.TitleMaxPixels, (settings, value) => settings with { TitleMaxPixels = (int)value }, AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX),
        new("descriptionMinPixels", PIXELS, 0, MAX_PIXELS, "A meta description narrower than this is flagged as too short.",
            settings => settings.DescriptionMinPixels, (settings, value) => settings with { DescriptionMinPixels = (int)value },
            AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX),
        new("descriptionMaxPixels", PIXELS, 1, MAX_PIXELS,
            "A meta description wider than this in search results is flagged as too long: Google's desktop results would likely cut it off. Measured in Arial at 14 px, as Google shows descriptions.",
            settings => settings.DescriptionMaxPixels, (settings, value) => settings with { DescriptionMaxPixels = (int)value },
            AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX),
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

    /// <summary>A minimum and the maximum that must be more than it, with what they're the widths of.</summary>
    private static readonly (string Min, string Max, string What)[] PAIRS =
    [
        ("titleMinPixels", "titleMaxPixels", "title"),
        ("descriptionMinPixels", "descriptionMaxPixels", "meta description")
    ];

    public static AnalysisLimit? Find(string id) => All.FirstOrDefault(limit => limit.Id == id);

    /// <summary>The other limit of a pair (a narrowest width and its widest), or null for a limit that stands alone.</summary>
    public static string? PartnerOf(string id) =>
        PAIRS.Where(pair => pair.Min == id || pair.Max == id).Select(pair => pair.Min == id ? pair.Max : pair.Min).FirstOrDefault();

    /// <summary>The settings' value of every limit, by its id, in the limits' order.</summary>
    public static Dictionary<string, long> Values(AnalysisSettings settings) => All.ToDictionary(limit => limit.Id, limit => limit.Get(settings), StringComparer.Ordinal);

    /// <summary>
    /// The engine's limits with these values in place of its own, and what's wrong with any of them, by the limit: one out of
    /// its range, a widest width that isn't more than its narrowest. A limit not named keeps the engine's value.
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
                errors[max] = [string.Create(CultureInfo.InvariantCulture, $"The widest {what} allowed must be more than the narrowest ({shortest:N0}).")];
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
