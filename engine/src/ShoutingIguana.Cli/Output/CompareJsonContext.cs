using System.Text.Json.Serialization;
using ShoutingIguana.Cli.Output.Models;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Output;

/// <summary><c>si compare --json</c>: camelCase, enums as camelCase names, as the sidecar serves its comparison.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters =
    [
        typeof(CamelCaseEnumConverter<Comparability>),
        typeof(CamelCaseEnumConverter<ComparisonDifferenceCode>),
        typeof(CamelCaseEnumConverter<ComparisonSide>),
        typeof(CamelCaseEnumConverter<OutOfDateReason>),
        typeof(CamelCaseEnumConverter<CheckChangeState>),
        typeof(CamelCaseEnumConverter<IssueSeverity>),
        typeof(CamelCaseEnumConverter<RowChangeState>),
        typeof(CamelCaseEnumConverter<GroupChangeState>),
        typeof(CamelCaseEnumConverter<NotCheckedCode>),
        typeof(CamelCaseEnumConverter<ScoreBand>)
    ])]
[JsonSerializable(typeof(CrawlComparison))]
[JsonSerializable(typeof(CheckComparison))]
internal sealed partial class CompareJsonContext : JsonSerializerContext
{
}
