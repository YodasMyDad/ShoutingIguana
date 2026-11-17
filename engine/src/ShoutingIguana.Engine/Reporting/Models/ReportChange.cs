using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>One check compared with the other crawl, with the words the report names it by.</summary>
/// <param name="Then">The URLs it flagged then; null when it didn't run then.</param>
/// <param name="Now">The URLs it flags now; null when it didn't run now.</param>
internal sealed record ReportChange(IssueDefinition Check, CheckWords Words, CheckChangeState State, long? Then, long? Now);
