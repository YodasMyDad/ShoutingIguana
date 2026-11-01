using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors.Models;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Library.Models;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// Every JSON the sidecar reads or writes: camelCase, enums as camelCase strings. Source-generated, as the engine's own
/// context (which is internal to the engine) is.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters =
    [
        typeof(CamelCaseEnumConverter<CrawlListStatus>),
        typeof(CamelCaseEnumConverter<CrawlNoticeLevel>),
        typeof(CamelCaseEnumConverter<CrawlStatus>),
        typeof(CamelCaseEnumConverter<IssueCategory>),
        typeof(CamelCaseEnumConverter<IssueSeverity>),
        typeof(CamelCaseEnumConverter<UserAgentPreset>)
    ])]
[JsonSerializable(typeof(ReadyEvent))]
[JsonSerializable(typeof(ProblemDto))]
[JsonSerializable(typeof(HealthDto))]
[JsonSerializable(typeof(ChecksDto))]
[JsonSerializable(typeof(CrawlOptionsDto))]
[JsonSerializable(typeof(DefaultsDto))]
[JsonSerializable(typeof(ValidationDto))]
[JsonSerializable(typeof(OpenCrawlRequest))]
[JsonSerializable(typeof(StartCrawlRequest))]
[JsonSerializable(typeof(ContinueCrawlRequest))]
[JsonSerializable(typeof(CrawlListItem))]
[JsonSerializable(typeof(IReadOnlyList<CrawlListItem>))]
[JsonSerializable(typeof(AlertDto))]
[JsonSerializable(typeof(IReadOnlyList<AlertDto>))]
[JsonSerializable(typeof(AnalysisDto))]
[JsonSerializable(typeof(CrawlStatsDto))]
[JsonSerializable(typeof(IssuesPageDto))]
[JsonSerializable(typeof(HelloEventDto))]
[JsonSerializable(typeof(ProgressEventDto))]
[JsonSerializable(typeof(CrawlEventDto))]
[JsonSerializable(typeof(LibraryFile))]
internal sealed partial class SidecarJsonContext : JsonSerializerContext
{
}
