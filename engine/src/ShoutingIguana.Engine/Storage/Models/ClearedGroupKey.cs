namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One cause of a check in the other crawl, by what names it there, as ids differ between files: its detail (or fault)
/// and its related URL's text. A duplicate cluster is named by its first page then.
/// </summary>
public sealed record ClearedGroupKey(string? Detail, string? RelatedUrl);
