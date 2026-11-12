using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One origin's robots.txt: what the origins list says of it, then the file line by line.</summary>
internal sealed record RobotsFileDto(
    string Origin,
    bool IsInternal,
    RobotsState? State,
    int? StatusCode,
    DateTime? FetchedAt,
    string? Error,
    long? Bytes,
    long Blocked,
    string UserAgent,
    IReadOnlyList<RobotsFileLine> Lines,
    RobotsUnmatched Unmatched,
    bool Respected);
