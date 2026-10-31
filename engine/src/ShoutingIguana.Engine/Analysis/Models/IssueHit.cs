namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One URL flagged with one issue.</summary>
/// <param name="Detail">A short explanation for this URL, e.g. "78 characters".</param>
/// <param name="RelatedUrlId">The other URL involved, when there is one: the broken target, the duplicate, the canonical.</param>
internal readonly record struct IssueHit(IssueDefinition Issue, int UrlId, string? Detail = null, int? RelatedUrlId = null);
