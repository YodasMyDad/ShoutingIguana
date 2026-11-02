using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Grouping"><c>detail</c>, <c>relatedUrl</c> or <c>none</c>, as <c>/api/checks</c> gives it.</param>
/// <param name="Total">The check's flagged URLs; null when the analysis skipped it, never 0.</param>
/// <param name="GroupCount">How many groups there are; null when the analysis skipped the check, never 0.</param>
internal sealed record IssueGroupPageDto(string CheckId, string Grouping, long? Total, long? GroupCount, int Offset, IReadOnlyList<IssueGroup> Groups);
