using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One origin's robots.txt as the crawl last read it, with its time in UTC as every time the API gives.</summary>
internal sealed record RobotsOriginDto(string Origin, bool IsInternal, RobotsState? State, int? StatusCode, DateTime? FetchedAt, string? Error, long? Bytes, long Blocked)
{
    public static RobotsOriginDto From(RobotsOrigin origin) =>
        new(origin.Origin, origin.IsInternal, origin.State, origin.StatusCode, origin.FetchedAt?.UtcDateTime, origin.Error, origin.Bytes, origin.Blocked);
}
