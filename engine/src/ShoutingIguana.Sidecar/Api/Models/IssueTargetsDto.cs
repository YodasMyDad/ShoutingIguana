using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>
/// The URLs each flagged page's issue names, in the order the pages were asked for; a page the check didn't flag is left
/// out, and a check the analysis skipped has none.
/// </summary>
internal sealed record IssueTargetsDto(string CheckId, IReadOnlyList<IssueTargetsOfUrlDto> Urls);
