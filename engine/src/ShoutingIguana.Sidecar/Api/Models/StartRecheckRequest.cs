using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary><c>POST …/rechecks</c>: what to recheck, and the secrets a crawl file doesn't keep, as Continue takes them.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record StartRecheckRequest : RecheckAskRequest
{
    /// <summary>The crawl's headers with their values: crawl files keep only the names.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>Needed again when the stored proxy had a user name and password.</summary>
    public string? ProxyUrl { get; init; }

    /// <summary>Recheck without the stored headers that weren't given again, instead of asking for them.</summary>
    public bool SkipMissingHeaders { get; init; }
}
