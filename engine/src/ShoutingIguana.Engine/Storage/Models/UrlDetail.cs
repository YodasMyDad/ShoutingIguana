using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Everything the crawl and its last analysis know about one URL, for the inspector (<see cref="UrlInspectorReader.Read"/>).</summary>
public sealed record UrlDetail
{
    public required int Id { get; init; }

    public required string Url { get; init; }

    /// <summary>
    /// The URL's length as the check for long URLs measures it (<see cref="Analysis.Rules.UrlRules.MeasuredLength"/>), to
    /// set against <see cref="ContentLimits.MaxUrlLength"/>; null for a stored URL that isn't one.
    /// </summary>
    public int? UrlLength { get; init; }

    public required bool IsInternal { get; init; }

    /// <summary>By the content type of a successful response, otherwise by the extension, as the checks tell files from pages.</summary>
    public required ResourceType Type { get; init; }

    public required UrlDiscovery Discovery { get; init; }

    /// <summary>Null for a URL never fetched.</summary>
    public UrlResponse? Response { get; init; }

    public required RedirectChain Redirects { get; init; }

    public required IndexabilityChecklist Indexability { get; init; }

    /// <summary>Null for anything that isn't a page whose HTML was parsed.</summary>
    public PageContent? Content { get; init; }

    public required ContentLimits Limits { get; init; }

    /// <summary>From the URL summary; null without one (<see cref="Summary"/>).</summary>
    public UrlLinkCounts? Links { get; init; }

    /// <summary>Every link to the URL and from it, of every kind: there with or without a URL summary.</summary>
    public required UrlLinkTotals LinkTotals { get; init; }

    /// <summary>As stored: names lowercased, for internal URLs only.</summary>
    public required IReadOnlyList<ResponseHeader> Headers { get; init; }

    public required IReadOnlyList<StructuredDataBlock> StructuredData { get; init; }

    public required IReadOnlyList<HreflangEntry> Hreflang { get; init; }

    /// <summary>The checks that flagged the URL in the last analysis, errors first; null before the first analysis.</summary>
    public IReadOnlyList<UrlIssue>? Issues { get; init; }

    /// <summary>The issues on other URLs that name this one as the other URL involved; null before the first analysis.</summary>
    public NamedByIssues? NamedBy { get; init; }

    /// <summary>
    /// The sitemaps that list the URL, read from the crawl's own tables: there with or without a URL summary, where
    /// <see cref="UrlLinkCounts.InSitemap"/> is the analysis's.
    /// </summary>
    public UrlSitemaps ListedIn { get; init; } = new(0, []);

    public required SummaryState Summary { get; init; }

    /// <summary>The crawl stored this URL's HTML (<see cref="UrlInspectorReader.ReadSource"/>).</summary>
    public required bool SourceStored { get; init; }
}
