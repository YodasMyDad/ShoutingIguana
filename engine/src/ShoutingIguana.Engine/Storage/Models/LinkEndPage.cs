namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One page of the URLs at one end of the links a view matches, each once, in the order the crawl found the URLs.
/// </summary>
/// <param name="Total">
/// How many URLs there are at that end: the URL grid's <c>matching</c> under the same links as its
/// <see cref="UrlQuery.LinkEnds"/>.
/// </param>
/// <param name="Links">The links the view matches: the links grid's <c>matching</c>.</param>
public sealed record LinkEndPage(LinkEnd End, long Total, long Links, int Offset, IReadOnlyList<LinkEndRow> Rows, SummaryState Summary);
