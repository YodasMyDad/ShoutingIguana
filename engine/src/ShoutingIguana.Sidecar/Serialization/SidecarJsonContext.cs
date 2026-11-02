using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
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
        typeof(CamelCaseEnumConverter<CrawlPhase>),
        typeof(CamelCaseEnumConverter<CrawlWaitReason>),
        typeof(CamelCaseEnumConverter<AnalysisPhase>),
        typeof(CamelCaseEnumConverter<CrawlStatus>),
        typeof(CamelCaseEnumConverter<IssueCategory>),
        typeof(CamelCaseEnumConverter<IssueSeverity>),
        typeof(CamelCaseEnumConverter<OutOfDateReason>),
        typeof(CamelCaseEnumConverter<UserAgentPreset>),

        // The grids (E3a).
        typeof(CamelCaseEnumConverter<SummaryUnavailableReason>),
        typeof(CamelCaseEnumConverter<ResourceType>),
        typeof(CamelCaseEnumConverter<DiscoverySource>),
        typeof(CamelCaseEnumConverter<LinkKind>),
        typeof(CamelCaseEnumConverter<LinkPosition>),
        typeof(CamelCaseEnumConverter<FetchMode>),
        typeof(CamelCaseEnumConverter<UrlState>),
        typeof(CamelCaseEnumConverter<SkipReason>),
        typeof(CamelCaseEnumConverter<UrlOutcome>),
        typeof(CamelCaseEnumConverter<RedirectVia>),
        typeof(CamelCaseEnumConverter<IndexabilityReason>),
        typeof(CamelCaseEnumConverter<IndexabilityJudge>),
        typeof(CamelCaseEnumConverter<IndexabilityTestKind>),
        typeof(CamelCaseEnumConverter<TestOutcome>)
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
[JsonSerializable(typeof(HistoryDto))]
[JsonSerializable(typeof(AnalysisDto))]
[JsonSerializable(typeof(CrawlStatsDto))]
[JsonSerializable(typeof(IssuesPageDto))]
[JsonSerializable(typeof(HelloEventDto))]
[JsonSerializable(typeof(ProgressEventDto))]
[JsonSerializable(typeof(AnalysisEventDto))]
[JsonSerializable(typeof(CrawlEventDto))]
[JsonSerializable(typeof(LibraryFile))]
[JsonSerializable(typeof(SummaryCacheFile))]

// The grids (E3a).
[JsonSerializable(typeof(IReadOnlyList<UrlColumnDto>))]
[JsonSerializable(typeof(GridQueryRequest))]
[JsonSerializable(typeof(UrlPageDto))]
[JsonSerializable(typeof(FacetsRequest))]
[JsonSerializable(typeof(FacetsDto))]
[JsonSerializable(typeof(UrlLookupRequest))]
[JsonSerializable(typeof(UrlRef))]
[JsonSerializable(typeof(UrlLookup))]
[JsonSerializable(typeof(UrlDetailDto))]
[JsonSerializable(typeof(LinksRequest))]
[JsonSerializable(typeof(LinkPageDto))]
[JsonSerializable(typeof(HtmlSourceDto))]
[JsonSerializable(typeof(GroupsRequest))]
[JsonSerializable(typeof(IssueGroupPageDto))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(long))]
internal sealed partial class SidecarJsonContext : JsonSerializerContext
{
}
