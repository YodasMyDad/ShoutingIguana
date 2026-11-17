using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Scalar columns of the responses table: what the grid sorts and filters on.</summary>
internal sealed record ResponseRow
{
    public required int UrlId { get; init; }
    public required long FetchedAt { get; init; }
    public required FetchOutcome Outcome { get; init; }
    public int? StatusCode { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public long? BodyBytes { get; init; }

    /// <summary>An image's own size in pixels, from its file's header; null for anything else.</summary>
    public int? PixelWidth { get; init; }

    /// <inheritdoc cref="PixelWidth"/>
    public int? PixelHeight { get; init; }
    public int TimeToFirstByteMs { get; init; }
    public int ResponseMs { get; init; }
    public string? HttpVersion { get; init; }
    public string? BlockReason { get; init; }
    public string? Error { get; init; }
    public bool IsHtml { get; init; }
    public string? Title { get; init; }
    public int TitleCount { get; init; }
    public string? MetaDescription { get; init; }
    public int MetaDescriptionCount { get; init; }
    public string? H1 { get; init; }
    public int H1Count { get; init; }
    public bool IsNoindex { get; init; }
    public bool IsNofollow { get; init; }
    public Indexability Indexability { get; init; }
    public IndexabilityReason IndexabilityReason { get; init; }
    public int? WordCount { get; init; }
    public ulong? ContentHash { get; init; }
    public ulong? SimHash { get; init; }
    public SimHashLanes? SimHashLanes { get; init; }
    public string? Language { get; init; }
}
