using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>One test, with only the facts that go with it.</summary>
internal sealed record IndexabilityTestDto(IndexabilityTestKind Test, TestOutcome Outcome)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StatusCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UrlTarget? MetaRefresh { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Unreachable { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RobotsBlockReason? Reason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rule { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Meta { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Header { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UrlTarget? Canonical { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalUrl { get; init; }

    public static IndexabilityTestDto From(IndexabilityTest test) => new(test.Test, test.Outcome)
    {
        StatusCode = test.StatusCode, MetaRefresh = test.MetaRefresh, Unreachable = test.Unreachable, Origin = test.Origin, Reason = test.Reason,
        Line = test.Line, Rule = test.Rule, Meta = test.Meta, Header = test.Header, Canonical = test.Canonical, CanonicalUrl = test.CanonicalUrl
    };
}
