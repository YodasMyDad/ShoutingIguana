using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL a comparison looked up by its text in one crawl: its id there, what the crawl made of it, and what says why.</summary>
internal readonly record struct FoundUrl(
    int Id,
    UrlReading Reading,
    FetchOutcome? Outcome,
    int? StatusCode,
    string? Error,
    string? BlockReason,
    UrlState State,
    SkipReason SkipReason);
