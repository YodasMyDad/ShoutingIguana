namespace ShoutingIguana.Engine.Analysis.Models;

/// <param name="Count">URLs flagged with the issue (0 = checked and clean).</param>
public sealed record IssueCount(IssueDefinition Issue, long Count);
