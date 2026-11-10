namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One page of a crawl's origins with their robots.txt.</summary>
internal sealed record RobotsOriginPageDto(long Total, int Offset, bool Respected, string UserAgent, long Blocked, IReadOnlyList<RobotsOriginDto> Origins);
