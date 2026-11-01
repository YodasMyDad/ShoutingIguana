using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

internal sealed record CheckDto(string Id, IssueCategory Category, IssueSeverity Severity, string Title, string Description, string Fix);
