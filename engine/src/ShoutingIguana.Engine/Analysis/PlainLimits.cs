using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The limits a plain text names. A text (<see cref="IssuePlainTexts"/>, <see cref="PlainWords"/>) may hold
/// <c>{limit:name}</c>, where the name is that of a threshold the rules read, as the analysis settings' JSON has it:
/// <c>{limit:titleMaxPixels}</c>, <c>{limit:largeImageBytes}</c>, <c>{limit:slowResponseMs}</c>. Filling the text puts
/// the setting's value there with its unit, as a sentence says it: "200 words", "200 KB", "1 second", and a width in
/// pixels as the characters it holds, "about 60 characters". So the words follow the thresholds: given the settings a crawl's analysis ran with (<see cref="Storage.Models.AnalysisInfo.Settings"/>),
/// they say the limits that crawl was judged by; given the settings an analysis would run with now, today's.
/// <para>
/// Every numeric threshold of <see cref="AnalysisSettings"/> has a name here, and a test holds the two together. A name
/// that is none of them is an error, which a test looks for in every plain text the engine has.
/// </para>
/// </summary>
public static partial class PlainLimits
{
    private const int BYTES_PER_UNIT = 1024;
    private const int MS_PER_SECOND = 1000;

    private static readonly string[] SIZE_UNITS = ["KB", "MB", "GB"];

    private static readonly Dictionary<string, (Func<AnalysisSettings, long> Value, LimitUnit Unit)> LIMITS = new(StringComparer.Ordinal)
    {
        [Name(nameof(AnalysisSettings.TitleMinPixels))] = (settings => settings.TitleMinPixels, LimitUnit.TitlePixels),
        [Name(nameof(AnalysisSettings.TitleMaxPixels))] = (settings => settings.TitleMaxPixels, LimitUnit.TitlePixels),
        [Name(nameof(AnalysisSettings.DescriptionMinPixels))] = (settings => settings.DescriptionMinPixels, LimitUnit.DescriptionPixels),
        [Name(nameof(AnalysisSettings.DescriptionMaxPixels))] = (settings => settings.DescriptionMaxPixels, LimitUnit.DescriptionPixels),
        [Name(nameof(AnalysisSettings.MinWordCount))] = (settings => settings.MinWordCount, LimitUnit.Words),
        [Name(nameof(AnalysisSettings.DuplicateMinWordCount))] = (settings => settings.DuplicateMinWordCount, LimitUnit.Words),
        [Name(nameof(AnalysisSettings.NearDuplicateMaxBits))] = (settings => settings.NearDuplicateMaxBits, LimitUnit.Bits),
        [Name(nameof(AnalysisSettings.ImageAltMaxLength))] = (settings => settings.ImageAltMaxLength, LimitUnit.Characters),
        [Name(nameof(AnalysisSettings.LargeImageBytes))] = (settings => settings.LargeImageBytes, LimitUnit.Bytes),
        [Name(nameof(AnalysisSettings.LargePageBytes))] = (settings => settings.LargePageBytes, LimitUnit.Bytes),
        [Name(nameof(AnalysisSettings.SlowResponseMs))] = (settings => settings.SlowResponseMs, LimitUnit.Milliseconds),
        [Name(nameof(AnalysisSettings.MaxUrlLength))] = (settings => settings.MaxUrlLength, LimitUnit.Characters),
        [Name(nameof(AnalysisSettings.MinInternalOutlinks))] = (settings => settings.MinInternalOutlinks, LimitUnit.Links),
        [Name(nameof(AnalysisSettings.MaxClickDepth))] = (settings => settings.MaxClickDepth, LimitUnit.Clicks),
        [Name(nameof(AnalysisSettings.MaxInternalLinks))] = (settings => settings.MaxInternalLinks, LimitUnit.Links),
        [Name(nameof(AnalysisSettings.MaxExternalLinks))] = (settings => settings.MaxExternalLinks, LimitUnit.Links),
        [Name(nameof(AnalysisSettings.SlowExternalLinkMs))] = (settings => settings.SlowExternalLinkMs, LimitUnit.Milliseconds)
    };

    /// <summary>What a limit is counted in, which says how a sentence gives it.</summary>
    private enum LimitUnit
    {
        Characters,

        /// <summary>A title's width, which a sentence gives as the ordinary characters it holds, never in pixels.</summary>
        TitlePixels,

        /// <summary>A description's width, as the ordinary characters it holds.</summary>
        DescriptionPixels,
        Words,
        Bits,
        Bytes,
        Milliseconds,
        Links,
        Clicks
    }

    /// <summary>The names a plain text can hold in <c>{limit:name}</c>: every numeric threshold of the analysis settings, as their JSON names it.</summary>
    public static IReadOnlyCollection<string> Names => LIMITS.Keys;

    /// <summary>
    /// The text with every <c>{limit:name}</c> in it replaced by that setting's value and unit: "Pages under
    /// {limit:minWordCount}" reads "Pages under 200 words". A text with none comes back as it is.
    /// </summary>
    /// <param name="settings">The thresholds to say: a crawl's analysis's own, or the ones an analysis would run with now.</param>
    /// <exception cref="ArgumentException">The text names a limit that isn't one of <see cref="Names"/>.</exception>
    public static string Fill(string text, AnalysisSettings settings) =>
        text.Contains('{') ? Token().Replace(text, match => Describe(match.Groups[1].Value, settings)) : text;

    /// <summary>A check's plain words with every limit in them filled (<see cref="Fill(string, AnalysisSettings)"/>); null for a check that has none.</summary>
    [return: NotNullIfNotNull(nameof(plain))]
    public static IssuePlainText? Fill(IssuePlainText? plain, AnalysisSettings settings) => plain is null
        ? null
        : plain with
        {
            Title = Fill(plain.Title, settings),
            What = [.. plain.What.Select(sentence => Fill(sentence, settings))],
            Fix = [.. plain.Fix.Select(step => Fill(step, settings))],
            Passed = Fill(plain.Passed, settings),
            OneFix = plain.OneFix is { } oneFix ? Fill(oneFix, settings) : null,
            LooksFor = plain.LooksFor is { } looksFor ? Fill(looksFor, settings) : null
        };

    /// <summary>A skip reason's plain words with every limit in them filled; null for a reason that has none.</summary>
    [return: NotNullIfNotNull(nameof(reason))]
    public static SkipReasonPlainText? Fill(SkipReasonPlainText? reason, AnalysisSettings settings) =>
        reason is null ? null : new SkipReasonPlainText(Fill(reason.Text, settings), Fill(reason.Action, settings));

    /// <summary>
    /// One limit as a sentence says it, with its unit: "about 60 characters", "200 words", "200 KB", "1 MB", "1 second",
    /// "1.5 seconds". Whole numbers are grouped in thousands, and nothing depends on the machine's culture.
    /// </summary>
    /// <param name="name">One of <see cref="Names"/>, such as <c>titleMaxPixels</c>.</param>
    /// <exception cref="ArgumentException">No analysis setting has that name.</exception>
    public static string Describe(string name, AnalysisSettings settings) => LIMITS.TryGetValue(name, out var limit)
        ? limit.Unit switch
        {
            LimitUnit.Characters => Count(limit.Value(settings), "character", "characters"),
            LimitUnit.TitlePixels => AboutCharacters(limit.Value(settings), AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX),
            LimitUnit.DescriptionPixels => AboutCharacters(limit.Value(settings), AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX),
            LimitUnit.Words => Count(limit.Value(settings), "word", "words"),
            LimitUnit.Bits => Count(limit.Value(settings), "bit", "bits"),
            LimitUnit.Bytes => Size(limit.Value(settings)),
            LimitUnit.Links => Count(limit.Value(settings), "link", "links"),
            LimitUnit.Clicks => Count(limit.Value(settings), "click", "clicks"),
            _ => Seconds(limit.Value(settings))
        }
        : throw new ArgumentException($"No analysis setting is named '{name}'. A plain text can hold {{limit:name}} for: {string.Join(", ", LIMITS.Keys)}.", nameof(name));

    /// <summary>
    /// A width in pixels as the ordinary characters it holds: "about 60 characters" for 580 px of title. To the nearest 5
    /// from 20 on, to the nearest whole below (<see cref="ApproximateCharacters"/>).
    /// </summary>
    private static string AboutCharacters(long pixels, double pixelsPerCharacter) =>
        $"about {Count(ApproximateCharacters(pixels, pixelsPerCharacter), "character", "characters")}";

    /// <summary>
    /// About how many ordinary characters fit in a width: 580 px of title is about 60, 200 about 20, 985 px of description
    /// about 160 and 400 about 65. The app's characters hint rounds the same way.
    /// </summary>
    public static long ApproximateCharacters(long pixels, double pixelsPerCharacter)
    {
        var characters = pixels / pixelsPerCharacter;
        return characters >= 20
            ? (long)Math.Round(characters / 5, MidpointRounding.AwayFromZero) * 5
            : (long)Math.Round(characters, MidpointRounding.AwayFromZero);
    }

    private static string Count(long value, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{value:N0} {(value == 1 ? one : many)}");

    /// <summary>"200 KB", "1 MB", "1.5 MB": the largest unit that leaves a number of 1 or more, to one decimal place at most.</summary>
    private static string Size(long bytes)
    {
        if (bytes < BYTES_PER_UNIT)
        {
            return Count(bytes, "byte", "bytes");
        }

        var value = bytes / (double)BYTES_PER_UNIT;
        var unit = 0;

        // By the rounded number, so 1,048,575 bytes is "1 MB" and never "1024 KB".
        while (Math.Round(value, 1) >= BYTES_PER_UNIT && unit < SIZE_UNITS.Length - 1)
        {
            value /= BYTES_PER_UNIT;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:#,0.#} {SIZE_UNITS[unit]}");
    }

    /// <summary>"1 second", "1.5 seconds", "0.8 seconds".</summary>
    private static string Seconds(long milliseconds) => milliseconds == MS_PER_SECOND
        ? "1 second"
        : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / (double)MS_PER_SECOND:#,0.###} seconds");

    /// <summary>A setting's name as the settings' JSON has it, which is the name a text uses.</summary>
    private static string Name(string property) => JsonNamingPolicy.CamelCase.ConvertName(property);

    [GeneratedRegex(@"\{limit:([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
