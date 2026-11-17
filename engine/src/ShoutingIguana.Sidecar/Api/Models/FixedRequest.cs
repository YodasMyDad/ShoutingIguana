using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What a check flagged in the other crawl and doesn't flag now (<c>POST …/issues/{checkId}/fixed</c>, the fix loop).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record FixedRequest
{
    /// <summary><c>previous</c> (when left out) or a crawl's id.</summary>
    public string? CompareWith { get; init; }

    /// <summary><c>rows</c> (when left out), each URL, or <c>groups</c>, each cause with no URL now.</summary>
    public string? Level { get; init; }

    /// <summary>Narrows to <c>fixed</c>, <c>gone</c> and <c>notChecked</c>; all three when left out.</summary>
    public IReadOnlyList<string>? States { get; init; }

    /// <summary>Narrows rows to one cause then.</summary>
    public ClearedGroupKeyDto? Group { get; init; }

    public int? Offset { get; init; }
    public int? Limit { get; init; }
}
