using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>A check that ran and found nothing, with its words: the report prints its good-news line.</summary>
internal sealed record ReportPassed(IssueDefinition Check, CheckWords Words);
