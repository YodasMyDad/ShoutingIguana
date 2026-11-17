namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// A <c>&lt;link rel="next"&gt;</c> or <c>rel="prev"</c> (<c>"previous"</c>) of the page's, in the head or not. The links table
/// has each as a link of kind pagination, but can't tell next from prev, which the pagination and canonical checks need.
/// </summary>
/// <param name="IsNext">It's a <c>rel="next"</c>; else a <c>rel="prev"</c> or <c>rel="previous"</c>.</param>
/// <param name="Url">The target, resolved and normalised, or null when the href isn't a valid http(s) URL.</param>
public sealed record PaginationFact(bool IsNext, string? Url);
