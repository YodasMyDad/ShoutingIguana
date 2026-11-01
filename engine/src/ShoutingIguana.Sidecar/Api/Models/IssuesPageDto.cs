namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Total">URLs the check flagged in all, or null when the last analysis skipped the check.</param>
internal sealed record IssuesPageDto(IReadOnlyList<IssueRowDto> Rows, long? Total);
