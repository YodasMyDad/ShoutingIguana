using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A URL's response, with its time in UTC as every time the API gives.</summary>
internal sealed record UrlResponseDto(
    bool Fetched,
    UrlOutcome? Outcome,
    int? StatusCode,
    string? ContentType,
    string? HttpVersion,
    DateTime CrawledAt,
    int? TtfbMs,
    int? ResponseMs,
    long? SizeBytes,
    long? BodyBytes,
    string? BlockReason,
    string? Error)
{
    public static UrlResponseDto From(UrlResponse response) => new(
        response.Fetched, response.Outcome, response.StatusCode, response.ContentType, response.HttpVersion, response.CrawledAt.UtcDateTime,
        response.TtfbMs, response.ResponseMs, response.SizeBytes, response.BodyBytes, response.BlockReason, response.Error);
}
