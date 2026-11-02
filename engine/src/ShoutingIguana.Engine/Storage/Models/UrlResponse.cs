namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL's stored response.</summary>
/// <param name="Fetched">False for a URL not checked, as its host had stopped answering: no request was made.</param>
/// <param name="TtfbMs">Null when no request was made.</param>
/// <param name="ResponseMs">Null when no request was made.</param>
/// <param name="SizeBytes">Bytes on the wire.</param>
/// <param name="BodyBytes">The body once unpacked, when it was downloaded.</param>
public sealed record UrlResponse(
    bool Fetched,
    UrlOutcome Outcome,
    int? StatusCode,
    string? ContentType,
    string? HttpVersion,
    DateTimeOffset CrawledAt,
    int? TtfbMs,
    int? ResponseMs,
    long? SizeBytes,
    long? BodyBytes,
    string? BlockReason,
    string? Error);
