namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// A filter of the URL grid by the links grid: the URLs that are one end of at least one link a set of link filters
/// matches. With <see cref="LinkEnd.From"/>, the pages that have such a link on them; with <see cref="LinkEnd.To"/>, the
/// URLs such a link leads to. So a view of the links grid opens its pages, or its targets, in the URL grid.
/// </summary>
/// <param name="Filters">The links grid's filters (<see cref="LinkGridReader.Columns"/>); none for every link.</param>
/// <param name="Search">The links grid's search.</param>
public sealed record UrlLinkEnds(LinkEnd End, IReadOnlyList<UrlFilter> Filters, string? Search = null);
