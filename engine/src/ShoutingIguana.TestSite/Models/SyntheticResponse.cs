namespace ShoutingIguana.TestSite.Models;

/// <param name="Headers">Extra response headers.</param>
/// <param name="DelayMs">Wait this long before answering, for slow-page tests.</param>
/// <param name="Abort">Drop the connection instead of answering, for unreachable-URL tests.</param>
public sealed record SyntheticResponse(
    int StatusCode,
    string ContentType,
    string? Text,
    byte[]? Bytes,
    string? Location,
    IReadOnlyDictionary<string, string>? Headers = null,
    int DelayMs = 0,
    bool Abort = false);
