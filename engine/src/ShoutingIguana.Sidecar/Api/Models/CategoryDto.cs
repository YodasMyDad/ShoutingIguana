using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Title">A plain name such as "Meta descriptions", shown as it is.</param>
internal sealed record CategoryDto(IssueCategory Id, string Title);
