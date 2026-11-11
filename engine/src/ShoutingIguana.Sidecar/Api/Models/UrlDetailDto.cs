using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>The inspector's URL: <see cref="UrlDetail"/> as JSON.</summary>
internal sealed record UrlDetailDto(
    int Id,
    string Url,
    int? UrlLength,
    bool IsInternal,
    ResourceType Type,
    UrlDiscovery Discovery,
    UrlResponseDto? Response,
    RedirectChain Redirects,
    IndexabilityDto Indexability,
    PageContent? Content,
    ContentLimits Limits,
    UrlLinkCounts? Links,
    UrlLinkTotals LinkTotals,
    IReadOnlyList<ResponseHeader> Headers,
    IReadOnlyList<StructuredDataBlock> StructuredData,
    IReadOnlyList<HreflangEntry> Hreflang,
    IReadOnlyList<UrlIssue>? Issues,
    NamedByIssues? NamedBy,
    SummaryState Summary,
    SourceStateDto Source);
