using System.Text.Json;
using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors.Models;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Exports.Models;
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

        // Written as names, and read from a name or a number: up to 2.0.3 these two went out as numbers and the names
        // the app sends were refused, so no extraction rule could be added and no rendering mode chosen.
        typeof(NamedOrNumberedEnumConverter<JavaScriptRendering>),
        typeof(NamedOrNumberedEnumConverter<ExtractionSelector>),

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
        typeof(CamelCaseEnumConverter<TestOutcome>),

        // Links, sitemaps and robots.txt (E3b).
        typeof(CamelCaseEnumConverter<IssueSubject>),
        typeof(CamelCaseEnumConverter<IssueReferences>),
        typeof(CamelCaseEnumConverter<IssueAbout>),
        typeof(CamelCaseEnumConverter<IssueRowOrder>),
        typeof(CamelCaseEnumConverter<SitemapDiscovery>),
        typeof(CamelCaseEnumConverter<SitemapSource>),
        typeof(CamelCaseEnumConverter<SitemapState>),
        typeof(CamelCaseEnumConverter<RobotsState>),
        typeof(CamelCaseEnumConverter<RobotsLineKind>),
        typeof(CamelCaseEnumConverter<RobotsLineProblem>),
        typeof(CamelCaseEnumConverter<RobotsBlockReason>),

        // Search and the favicon (E6).
        typeof(CamelCaseEnumConverter<UrlSearchRank>),
        typeof(CamelCaseEnumConverter<UrlSearchField>),
        typeof(CamelCaseEnumConverter<LinkEnd>),

        // Export (E5).
        typeof(CamelCaseEnumConverter<ExportKind>),
        typeof(CamelCaseEnumConverter<ExportFormat>),
        typeof(CamelCaseEnumConverter<ExportStatus>),

        // The score and plain words (round 3).
        typeof(CamelCaseEnumConverter<ScoreBand>),
        typeof(CamelCaseEnumConverter<ScoreExtent>),
        typeof(CamelCaseEnumConverter<ScoreUnit>),
        typeof(CamelCaseEnumConverter<ScoreIncompleteReason>),
        typeof(CamelCaseEnumConverter<NoScoreReason>),
        typeof(CamelCaseEnumConverter<IssueFixer>),

        // The proxy test (round 5).
        typeof(CamelCaseEnumConverter<ProxyTestOutcome>),

        // The fix loop.
        typeof(CamelCaseEnumConverter<Comparability>),
        typeof(CamelCaseEnumConverter<ComparisonDifferenceCode>),
        typeof(CamelCaseEnumConverter<ComparisonSide>),
        typeof(CamelCaseEnumConverter<CheckChangeState>),
        typeof(CamelCaseEnumConverter<RowChangeState>),
        typeof(CamelCaseEnumConverter<GroupChangeState>),
        typeof(CamelCaseEnumConverter<NotCheckedCode>),
        typeof(CamelCaseEnumConverter<RecheckKind>),
        typeof(CamelCaseEnumConverter<RecheckStatus>),
        typeof(CamelCaseEnumConverter<RecheckRole>),
        typeof(CamelCaseEnumConverter<RecheckOutcome>),
        typeof(CamelCaseEnumConverter<ProgressPhase>)
    ])]
[JsonSerializable(typeof(ReadyEvent))]
[JsonSerializable(typeof(ProblemDto))]
[JsonSerializable(typeof(HealthDto))]
[JsonSerializable(typeof(ChecksDto))]
[JsonSerializable(typeof(CrawlOptionsDto))]
[JsonSerializable(typeof(DefaultsDto))]
[JsonSerializable(typeof(ValidationDto))]
[JsonSerializable(typeof(StartUrlRequest))]
[JsonSerializable(typeof(StartUrlDto))]
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
[JsonSerializable(typeof(ShopifyAccessDto))]
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
[JsonSerializable(typeof(UrlRequest))]
[JsonSerializable(typeof(UrlLookup))]
[JsonSerializable(typeof(UrlDetailDto))]
[JsonSerializable(typeof(LinksRequest))]
[JsonSerializable(typeof(LinkPageDto))]
[JsonSerializable(typeof(FoundOnRequest))]
[JsonSerializable(typeof(FoundOnDto))]
[JsonSerializable(typeof(IssueTargetsDto))]
[JsonSerializable(typeof(RedirectChain))]
[JsonSerializable(typeof(RobotsRefusal))]
[JsonSerializable(typeof(HtmlSourceDto))]
[JsonSerializable(typeof(GroupsRequest))]
[JsonSerializable(typeof(IssueGroupPageDto))]

// Links, sitemaps and robots.txt (E3b).
[JsonSerializable(typeof(IReadOnlyList<LinkColumnDto>))]
[JsonSerializable(typeof(LinkGridPageDto))]
[JsonSerializable(typeof(SitemapList))]
[JsonSerializable(typeof(RobotsOriginPageDto))]
[JsonSerializable(typeof(RobotsFileRequest))]
[JsonSerializable(typeof(RobotsFileDto))]
[JsonSerializable(typeof(RobotsBlockedRequest))]
[JsonSerializable(typeof(RobotsBlockedPage))]

// Search and the favicon (E6).
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(RobotsOriginsRequest))]
[JsonSerializable(typeof(LinkEndsRequest))]
[JsonSerializable(typeof(LinkEndPage))]

// Links grouped by target (step 3b).
[JsonSerializable(typeof(LinkGroupsRequest))]
[JsonSerializable(typeof(LinkGroupPageDto))]
[JsonSerializable(typeof(UrlImagesRequest))]
[JsonSerializable(typeof(PageImages))]
[JsonSerializable(typeof(AnalysisSettingsDto))]
[JsonSerializable(typeof(ResetAnalysisSettingsRequest))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(UrlSearchResults))]

// Export (E5).
[JsonSerializable(typeof(ExportRequestDto))]
[JsonSerializable(typeof(ExportDto))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(long))]
// The proxy test (round 5).
[JsonSerializable(typeof(ProxyTestRequest))]
[JsonSerializable(typeof(ProxyTestResult))]

// The fix loop.
[JsonSerializable(typeof(ChangesDto))]
[JsonSerializable(typeof(NoComparisonDto))]
[JsonSerializable(typeof(RowChange))]
[JsonSerializable(typeof(FixedRequest))]
[JsonSerializable(typeof(FixedPageDto))]
[JsonSerializable(typeof(RecheckAskRequest))]
[JsonSerializable(typeof(StartRecheckRequest))]
[JsonSerializable(typeof(RecheckPlanDto))]
[JsonSerializable(typeof(RechecksDto))]
[JsonSerializable(typeof(RecheckDto))]
[JsonSerializable(typeof(RecheckRowsRequest))]
[JsonSerializable(typeof(RecheckRowPage))]
[JsonSerializable(typeof(RecheckEventDto))]
[JsonSerializable(typeof(ExtractionRequest))]
[JsonSerializable(typeof(ExtractionPageDto))]
internal sealed partial class SidecarJsonContext : JsonSerializerContext
{
}
