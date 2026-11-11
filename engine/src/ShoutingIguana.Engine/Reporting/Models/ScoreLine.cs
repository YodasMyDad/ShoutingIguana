using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>One check that found something, as the score counts it: its count, what that's a share of, the band, and the points it takes off.</summary>
/// <param name="Of">The pages read or the addresses checked, by <paramref name="Unit"/>.</param>
internal sealed record ScoreLine(string CheckId, IssueSeverity Severity, long Count, long Of, ScoreUnit Unit, ScoreExtent Extent, int Points);
