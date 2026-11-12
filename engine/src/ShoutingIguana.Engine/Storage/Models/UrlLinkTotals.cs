namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// Every link to a URL and from it, of every kind, a page's links to itself left out: what the inspector's lists of its
/// links hold in all (<see cref="UrlInspectorReader.ReadLinks"/> with no kinds named). Counted from the links themselves,
/// so it needs no URL summary, where <see cref="UrlLinkCounts"/> (hyperlinks, on the pages audited) does.
/// </summary>
public sealed record UrlLinkTotals(long Inlinks, long Outlinks)
{
    /// <summary>
    /// The hyperlinks among <see cref="Inlinks"/>: every stored hyperlink to the URL, its own to itself left out, which is
    /// what the links grid counts for it and what the inspector's list gives for that kind alone. The URL summary's
    /// <see cref="UrlLinkCounts.Inlinks"/> counts the hyperlinks on the pages the analysis audited, so it's less than this
    /// by the hyperlinks on parsed pages the analysis leaves out: Cloudflare's own, under <c>/cdn-cgi/</c>.
    /// </summary>
    public long HyperlinkInlinks { get; init; }
}
