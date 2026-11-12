using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>A check the analysis couldn't run, with why and what would let it run, in the report's words.</summary>
/// <param name="ReasonCode">The analysis's reason, or null when the engine that ran it stored none.</param>
internal sealed record ReportSkipped(IssueDefinition Check, CheckWords Words, string? ReasonCode, string Why, string? Action);
