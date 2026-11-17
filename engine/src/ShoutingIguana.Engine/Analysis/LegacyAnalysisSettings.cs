using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The title and description limits as they were saved before they went from characters to pixels, under their old
/// names (<c>titleMaxLength</c>), and what each is now (<c>titleMaxPixels</c>). A limit the user changed keeps their
/// intent: its characters times an ordinary character's width (<see cref="AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX"/>,
/// 9.4 px, and <see cref="AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX"/>, 6.2 px), so 70 characters is 658 px. A
/// limit at the old default (60 characters) takes the new default (580 px), as a limit never changed follows the engine.
/// </summary>
public static class LegacyAnalysisSettings
{
    private static readonly Dictionary<string, (string Name, int OldDefault, int NewDefault, double PixelsPerCharacter)> RENAMED = new(StringComparer.Ordinal)
    {
        ["titleMinLength"] = (Name(nameof(AnalysisSettings.TitleMinPixels)), 30, new AnalysisSettings().TitleMinPixels, AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX),
        ["titleMaxLength"] = (Name(nameof(AnalysisSettings.TitleMaxPixels)), 60, new AnalysisSettings().TitleMaxPixels, AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX),
        ["descriptionMinLength"] =
            (Name(nameof(AnalysisSettings.DescriptionMinPixels)), 70, new AnalysisSettings().DescriptionMinPixels, AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX),
        ["descriptionMaxLength"] =
            (Name(nameof(AnalysisSettings.DescriptionMaxPixels)), 155, new AnalysisSettings().DescriptionMaxPixels, AnalysisSettings.TYPICAL_DESCRIPTION_CHARACTER_PX)
    };

    /// <summary>The old names, as the settings' JSON had them.</summary>
    public static IReadOnlyCollection<string> Names => RENAMED.Keys;

    /// <summary>A limit saved under an old name as it is now: its new name and its value in pixels; false for any other name.</summary>
    public static bool TryConvert(string name, long characters, out string pixelsName, out long pixels)
    {
        if (!RENAMED.TryGetValue(name, out var renamed))
        {
            (pixelsName, pixels) = (name, characters);
            return false;
        }

        pixelsName = renamed.Name;
        pixels = characters == renamed.OldDefault
            ? renamed.NewDefault
            : (long)Math.Round(characters * renamed.PixelsPerCharacter, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Settings as JSON with each old name replaced by its new one and its value converted (<see cref="TryConvert"/>),
    /// unless the new name is there too, which wins. A value that isn't a whole number is left for the reader to refuse.
    /// </summary>
    public static JsonObject Upgrade(JsonObject settings)
    {
        foreach (var name in settings.Select(property => property.Key).Where(RENAMED.ContainsKey).ToList())
        {
            var value = settings[name];
            settings.Remove(name);
            if (value is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue<long>(out var characters) &&
                TryConvert(name, characters, out var pixelsName, out var pixels) && !settings.ContainsKey(pixelsName))
            {
                settings[pixelsName] = pixels;
            }
        }

        return settings;
    }

    private static string Name(string property) => JsonNamingPolicy.CamelCase.ConvertName(property);
}
