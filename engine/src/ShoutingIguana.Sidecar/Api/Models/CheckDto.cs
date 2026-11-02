using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Grouping">How the check's issues group (E3a): <c>detail</c>, <c>relatedUrl</c> or <c>none</c>.</param>
internal sealed record CheckDto(string Id, IssueCategory Category, IssueSeverity Severity, string Title, string Description, string Fix, string Grouping);
