namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>Checks that found at least one issue, by severity: the rows of the issues screen, not the flagged URLs.</summary>
internal sealed record IssueCountsDto(long Error, long Warning, long Notice);
