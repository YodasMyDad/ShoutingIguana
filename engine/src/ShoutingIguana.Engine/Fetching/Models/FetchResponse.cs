namespace ShoutingIguana.Engine.Fetching.Models;

internal sealed record FetchResponse
{
    public required Uri RequestUri { get; init; }
    public required FetchOutcome Outcome { get; init; }
    public int? StatusCode { get; init; }
    public IReadOnlyList<ResponseHeader> Headers { get; init; } = [];

    /// <summary>Lowercase media type without parameters, e.g. <c>text/html</c>.</summary>
    public string? ContentType { get; init; }
    public bool IsHtml { get; init; }

    /// <summary>Content-Length header value: the size on the wire.</summary>
    public long? ContentLength { get; init; }

    /// <summary>Bytes read off the wire, before decompression. Null when the body was not read.</summary>
    public long? TransferBytes { get; init; }

    /// <summary>Decoded body size. Null when the body was not read.</summary>
    public long? BodyBytes { get; init; }

    /// <summary>The decoded body, when one was requested and the response qualified.</summary>
    public string? Body { get; init; }
    public bool BodyTruncated { get; init; }

    /// <summary>Absolute redirect target from the Location header.</summary>
    public Uri? Location { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public string? HttpVersion { get; init; }
    public int TimeToFirstByteMs { get; init; }
    public int TotalMs { get; init; }
    public string? BlockReason { get; init; }
    public string? Error { get; init; }

    /// <summary>A network-level failure that is worth retrying.</summary>
    public bool IsTransientFailure { get; init; }
}
