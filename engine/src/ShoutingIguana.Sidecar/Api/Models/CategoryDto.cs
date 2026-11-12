using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <param name="Title">A plain name such as "Meta descriptions", shown as it is.</param>
/// <param name="PlainTitle">The category in plain words, such as "Search descriptions"; null while it has none.</param>
internal sealed record CategoryDto(IssueCategory Id, string Title, string? PlainTitle);
