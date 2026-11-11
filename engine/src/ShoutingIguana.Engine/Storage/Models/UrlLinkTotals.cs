namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Every link to a URL and from it, of every kind, a page's links to itself left out: what the inspector's lists of its
/// links hold in all (<see cref="UrlInspectorReader.ReadLinks"/> with no kinds named). Counted from the links themselves,
/// so it needs no URL summary, where <see cref="UrlLinkCounts"/> (hyperlinks, on the pages audited) does.
/// </summary>
public sealed record UrlLinkTotals(long Inlinks, long Outlinks);
