E = 'src/ShoutingIguana.Engine/'

EDITS = [
(E + 'Storage/Models/UrlLinkTotals.cs',
'''public sealed record UrlLinkTotals(long Inlinks, long Outlinks);
''',
'''public sealed record UrlLinkTotals(long Inlinks, long Outlinks)
{
    /// <summary>
    /// The hyperlinks among <see cref="Inlinks"/>: every stored hyperlink to the URL, its own to itself left out, which is
    /// what the links grid counts for it and what the inspector's list gives for that kind alone. The URL summary's
    /// <see cref="UrlLinkCounts.Inlinks"/> counts the hyperlinks on the pages the analysis audited, so it's less than this
    /// by the hyperlinks on parsed pages the analysis leaves out: Cloudflare's own, under <c>/cdn-cgi/</c>.
    /// </summary>
    public long HyperlinkInlinks { get; init; }
}
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''            LinkTotals = new UrlLinkTotals(TotalLinks(context, LinkDirection.Inlinks, urlId), TotalLinks(context, LinkDirection.Outlinks, urlId)),
''',
'''            LinkTotals = new UrlLinkTotals(TotalLinks(context, LinkDirection.Inlinks, urlId), TotalLinks(context, LinkDirection.Outlinks, urlId))
            {
                HyperlinkInlinks = TotalLinks(context, LinkDirection.Inlinks, urlId, [LinkKind.Hyperlink])
            },
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''        var (near, far) = direction == LinkDirection.Inlinks ? ("to_id", "from_id") : ("from_id", "to_id");
        var kinds = query.Kinds.Distinct().Order().ToList();
        var ofKinds = kinds.Count == 0 ? string.Empty : $" AND l.kind IN ({string.Join(", ", kinds.Select(kind => (int)kind))})";
        var where = $"l.{near} = $id AND l.{far} <> $id{ofKinds}";
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        var hash = $"{direction}|{urlId}|{ofKinds}";
        var total = kinds.Count == 0 ? TotalLinks(context, direction, urlId) : context.Count($"links|{hash}", () => Count(context, direction, urlId, kinds, ofKinds));
''',
'''        var (near, far) = direction == LinkDirection.Inlinks ? ("to_id", "from_id") : ("from_id", "to_id");
        var kinds = query.Kinds.Distinct().Order().ToList();
        var ofKinds = OfKinds(kinds);
        var where = $"l.{near} = $id AND l.{far} <> $id{ofKinds}";
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        var hash = $"{direction}|{urlId}|{ofKinds}";
        var total = TotalLinks(context, direction, urlId, kinds);
'''),
(E + 'Storage/UrlInspectorReader.cs',
'''    private static long TotalLinks(GridReadContext context, LinkDirection direction, int urlId) =>
        context.Count($"links|{direction}|{urlId}|", () => Count(context, direction, urlId, [], string.Empty));

    private static long Count(GridReadContext context, LinkDirection direction, int urlId, IReadOnlyList<LinkKind> kinds, string ofKinds)
    {
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];

        // Every link to the URL, where the link summary counts the crawl as it stands: it has them by target, a page's
        // links to itself apart, in a few rows, where the links' index has an entry a link (a home page's million).
        if (direction == LinkDirection.Inlinks && kinds.Count == 0 && context.LinkSummary is not null)
        {
            return context.Scalar("SELECT COALESCE(SUM(links), 0) FROM link_summary WHERE to_id = $id AND self = 0", parameters);
        }
''',
'''    private static long TotalLinks(GridReadContext context, LinkDirection direction, int urlId) => TotalLinks(context, direction, urlId, []);

    /// <summary>The same of some kinds only (in order, each once): one count, kept, for the inspector's own figure and its list of those kinds.</summary>
    private static long TotalLinks(GridReadContext context, LinkDirection direction, int urlId, IReadOnlyList<LinkKind> kinds) =>
        context.Count($"links|{direction}|{urlId}|{OfKinds(kinds)}", () => Count(context, direction, urlId, kinds, OfKinds(kinds)));

    /// <summary>The condition that a link <c>l</c> is of one of these kinds; none for every kind.</summary>
    private static string OfKinds(IReadOnlyList<LinkKind> kinds) =>
        kinds.Count == 0 ? string.Empty : $" AND l.kind IN ({string.Join(", ", kinds.Select(kind => (int)kind))})";

    private static long Count(GridReadContext context, LinkDirection direction, int urlId, IReadOnlyList<LinkKind> kinds, string ofKinds)
    {
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];

        // The links to the URL, of every kind or of some, where the link summary counts the crawl as it stands: it has
        // them by target and kind, a page's links to itself apart, in a few rows, where the links' index has an entry a
        // link (a home page's million). It is what the links grid counts from, so the two can't differ.
        if (direction == LinkDirection.Inlinks && context.LinkSummary is not null)
        {
            return context.Scalar($"SELECT COALESCE(SUM(l.links), 0) FROM link_summary l WHERE l.to_id = $id AND l.self = 0{ofKinds}", parameters);
        }
'''),
]
