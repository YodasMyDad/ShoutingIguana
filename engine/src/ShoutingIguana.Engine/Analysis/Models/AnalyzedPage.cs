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

    public IndexabilityReason IndexabilityReason { get; init; }

    public bool IsIndexable => IndexabilityReason == IndexabilityReason.None;

    public IReadOnlyList<ResponseHeader> Headers { get; init; } = [];

    public required PageFacts Facts { get; init; }

    public IReadOnlyList<StoredLink> Links { get; init; } = [];

    public string? Header(string name) => Headers.FirstOrDefault(header => header.Name == name).Value;
}
