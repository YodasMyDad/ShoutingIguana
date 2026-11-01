using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>The fields of the crawl options DTO, which validation keys its messages by: the JSON names of its properties.</summary>
internal static class OptionFields
{
    public const string START_URL = "startUrl";
    public const string EXTRA_HEADERS = "extraHeaders";

    private static readonly HashSet<string> NAMES =
        [.. SidecarJsonContext.Default.CrawlOptionsDto.Properties.Select(property => property.Name)];

    public static bool IsField(string name) => NAMES.Contains(name);

    /// <summary>Field errors from several checks, each field's messages in the order they came.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Merge(params IReadOnlyDictionary<string, IReadOnlyList<string>>[] errors) =>
        errors.SelectMany(found => found)
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, IReadOnlyList<string> (group) => [.. group.SelectMany(field => field.Value)], StringComparer.Ordinal);
}
