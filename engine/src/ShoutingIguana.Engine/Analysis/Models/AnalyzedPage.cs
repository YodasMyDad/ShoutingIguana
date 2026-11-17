using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One successful internal HTML page, as the rules see it: the stored facts, headers and outgoing links.</summary>
internal sealed record AnalyzedPage
{
    public required int UrlId { get; init; }

    /// <summary>The normalised URL as stored, which is what canonical and hreflang URLs are compared with.</summary>
    public required string Url { get; init; }

    public required Uri Uri { get; init; }

    public int Depth { get; init; }

    /// <summary>Bytes on the wire, compressed.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Decoded HTML size.</summary>
    public long? BodyBytes { get; init; }

    public int TimeToFirstByteMs { get; init; }

    public int ResponseMs { get; init; }

    /// <summary>When the crawl fetched the page: what a date in its robots rules is compared with.</summary>
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>As search engines will see it, which can differ from what the crawl recorded (see <see cref="UrlTable"/>).</summary>
    public IndexabilityReason IndexabilityReason { get; init; }

    public bool IsIndexable => IndexabilityReason == IndexabilityReason.None;

    /// <summary>The page moves on with an instant meta refresh, so search engines treat it as a redirect, not a page.</summary>
    public bool IsRedirect => IndexabilityReason == IndexabilityReason.Redirected;

    public IReadOnlyList<ResponseHeader> Headers { get; init; } = [];

    public required PageFacts Facts { get; init; }

    public IReadOnlyList<StoredLink> Links { get; init; } = [];

    public string? Header(string name) => Headers.FirstOrDefault(header => header.Name == name).Value;
}
