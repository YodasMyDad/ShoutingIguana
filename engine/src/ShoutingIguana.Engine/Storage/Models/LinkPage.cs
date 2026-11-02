namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One page of the links to or from a URL.</summary>
/// <param name="Offset">Where the page starts, or null when it was read on from a cursor.</param>
/// <param name="Total">The links of the kinds asked for.</param>
/// <param name="Next">The cursor for the link after the last one, or null at the end.</param>
public sealed record LinkPage(IReadOnlyList<LinkRow> Rows, int? Offset, long Total, string? Next);
