using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Grouping"><c>detail</c>, <c>relatedUrl</c> or <c>none</c>, as <c>/api/checks</c> gives it.</param>
/// <param name="Total">The check's flagged URLs; null when the analysis skipped it, never 0.</param>
/// <param name="GroupCount">How many groups there are; null when the analysis skipped the check, never 0.</param>
internal sealed record IssueGroupPageDto(string CheckId, string Grouping, long? Total, long? GroupCount, int Offset, IReadOnlyList<IssueGroup> Groups)
{
    /// <summary>The order the groups are in (evidence): <c>pages</c> or <c>size</c>.</summary>
    public string Sort { get; init; } = "pages";

    /// <summary>For a check that doesn't group, that it flagged nearly every page audited: probably the site's template (evidence).</summary>
    public bool LikelyTemplate { get; init; }

    /// <summary>With <c>compareWith</c> (the fix loop): what the groups were compared with, each group then having its <c>change</c>. Left out without one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GroupsComparedDto? Compared { get; init; }
}
