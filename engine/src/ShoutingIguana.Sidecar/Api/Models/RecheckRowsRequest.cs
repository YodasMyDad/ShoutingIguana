using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A page of one recheck's URLs, narrowed by outcome and role (all of each when left out).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RecheckRowsRequest
{
    /// <summary><c>fixed</c>, <c>still</c>, <c>notChecked</c>, <c>notRechecked</c>, <c>new</c>.</summary>
    public IReadOnlyList<string>? Outcomes { get; init; }

    /// <summary><c>judged</c>, <c>named</c>, <c>referring</c>, <c>new</c>, <c>letGo</c>.</summary>
    public IReadOnlyList<string>? Roles { get; init; }

    public int? Offset { get; init; }
    public int? Limit { get; init; }
}
