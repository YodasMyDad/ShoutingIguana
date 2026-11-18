using System.Globalization;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// Where to fix what a check flags, for a page of its URLs in one read: for a URL other URLs refer to (a 404, a redirect, a
/// sitemap entry), the pages that link to it and the sitemaps that list it (<see cref="ReadFoundOn"/>); for a page holding
/// bad references, the URLs its issue names, each with the page's link to it (<see cref="ReadTargets"/>). Which a check
/// wants is the catalog's to say (<see cref="IssueDefinition.References"/>). Both read in one snapshot, a URL at a time on
/// the links' indexes, so a page of a hundred URLs costs what their own links do, never a pass over the crawl's.
/// </summary>
public static class FoundOnReader
{
    /// <summary>The most URLs one read takes: a page of a grid, or of a report's list.</summary>
    public const int MAX_URLS = 100;

    /// <summary>The most references one read lists of each URL: the rest are the Links screen's.</summary>
    public const int MAX_LISTED = 50;

    /// <summary>What a read lists of each URL when it isn't told.</summary>
    public const int DEFAULT_LISTED = 5;

    /// <summary>
    /// The most links to a URL read to count the pages they're on. Past it the count is a floor (<see cref="UrlFoundOn.PagesExact"/>):
    /// a link in every page's footer has a million links to it, and the links' index by target holds no page.
    /// </summary>
    public const int PAGES_COUNTED_LINKS = 5_000;

    /// <summary>Tests only: <see cref="PAGES_COUNTED_LINKS"/>, lowered to reach past it on a small crawl.</summary>
    internal static int PagesCountedLinks { get; set; } = PAGES_COUNTED_LINKS;

    /// <summary>
    /// For each URL the crawl has, of those asked for (in the order asked, each once): how many links lead to it and how many
    /// pages they're on, the first <paramref name="listed"/> of them in crawl order (each naming the page it's on, with its
    /// kind, text, position and whether it's followed), the sitemaps that list it, and whether it's the crawl's start URL.
    /// A page's links to itself are left out, as the inspector leaves them. An id the crawl doesn't have is left out.
    /// </summary>
    /// <exception cref="UrlQueryException">Too many URLs, or too many listed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IReadOnlyList<UrlFoundOn> ReadFoundOn(string databasePath, IReadOnlyList<int> urlIds, int listed = DEFAULT_LISTED, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        Validate(urlIds, listed);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => urlIds.Distinct().Select(id => FoundOn(context, id, listed)).OfType<UrlFoundOn>().ToList());
    }

    /// <summary>
    /// For each page asked for that the check flagged (in the order asked, each once): every URL its issue names, counted,
    /// and the first <paramref name="listed"/> in the order of their ids (the crawl's order of finding them), each with its
    /// status and the page's first link to it (a canonical check's canonical, an image check's image first). Null when the
    /// crawl has never been analysed; empty for a check the analysis skipped, or one this engine doesn't know.
    /// </summary>
    /// <exception cref="UrlQueryException">Too many URLs, or too many listed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IReadOnlyList<IssueTargetsOfUrl>? ReadTargets(string databasePath, string checkId, IReadOnlyList<int> urlIds, int listed = DEFAULT_LISTED,
        ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        Validate(urlIds, listed);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context =>
        {
            if (context.Analysis is null)
            {
                return null;
            }

            // A check the analysis didn't run (skipped, or another engine's) names nothing, whatever rows a file holds.
            if (context.Scalar("SELECT COUNT(*) FROM issue_types WHERE id = $check", [new("$check", checkId)]) == 0)
            {
                return [];
            }

            return (IReadOnlyList<IssueTargetsOfUrl>)[.. urlIds.Distinct().Select(id => Targets(context, checkId, id, listed)).OfType<IssueTargetsOfUrl>()];
        });
    }

    private static void Validate(IReadOnlyList<int> urlIds, int listed)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        if (urlIds.Count is 0 or > MAX_URLS)
        {
            errors["urlIds"] = [$"Give from 1 to {MAX_URLS.ToString(CultureInfo.InvariantCulture)} URL ids."];
        }

        if (listed is < 0 or > MAX_LISTED)
        {
            errors["limit"] = [$"List from 0 to {MAX_LISTED.ToString(CultureInfo.InvariantCulture)} of each URL's."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
    }

    internal static UrlFoundOn? FoundOn(GridReadContext context, int urlId, int listed)
    {
        long source;
        using (var command = context.Command("SELECT source FROM urls WHERE id = $id", [new("$id", urlId)]))
        {
            if (command.ExecuteScalar() is not long stored)
            {
                return null;
            }

            source = stored;
        }

        if (UrlInspectorReader.ReadLinks(context, urlId, LinkDirection.Inlinks, new LinkQuery { Limit = Math.Max(listed, 1) }) is not { } page)
        {
            return null;
        }

        var (pages, exact) = Pages(context, urlId, page.Total);
        return new UrlFoundOn(urlId, page.Total, pages, exact, [.. page.Rows.Take(listed)], Sitemaps(context, urlId), source == (long)DiscoverySource.Seed);
    }

    /// <summary>
    /// The pages linking to the URL: counted on its links when they're few enough to read; otherwise the more of the pages
    /// the first of them are on and the pages the URL summary counts its hyperlinks on, a floor either way.
    /// </summary>
    private static (long Pages, bool Exact) Pages(GridReadContext context, int urlId, long links)
    {
        if (links == 0)
        {
            return (0, true);
        }

        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        var seen = context.Count($"found-on pages|{urlId}|{PagesCountedLinks}", () => context.Scalar($"""
            SELECT COUNT(DISTINCT from_id) FROM (SELECT l.from_id FROM links l WHERE l.to_id = $id AND l.from_id <> $id
                                                 LIMIT {PagesCountedLinks.ToString(CultureInfo.InvariantCulture)})
            """, parameters));
        if (links <= PagesCountedLinks)
        {
            return (seen, true);
        }

        var hyperlinked = context.Mode == GridMode.Summary
            ? context.Scalar("SELECT COALESCE((SELECT linking_pages FROM url_summary WHERE url_id = $id), 0)", parameters)
            : 0;
        return (Math.Max(seen, hyperlinked), false);
    }

    /// <summary>The sitemaps listing the URL; none asked for where the URL summary says no sitemap lists it.</summary>
    private static UrlSitemaps Sitemaps(GridReadContext context, int urlId)
    {
        if (context.Mode == GridMode.Summary &&
            context.Scalar("SELECT COALESCE((SELECT in_sitemap FROM url_summary WHERE url_id = $id), -1)", [new("$id", urlId)]) == 0)
        {
            return new UrlSitemaps(0, []);
        }

        return UrlInspectorReader.ListedIn(context, urlId);
    }

    private static IssueTargetsOfUrl? Targets(GridReadContext context, string checkId, int urlId, int listed)
    {
        KeyValuePair<string, object>[] parameters = [new("$check", checkId), new("$id", urlId)];
        if (context.Scalar("SELECT COUNT(*) FROM issues WHERE issue = $check AND url_id = $id", parameters) == 0)
        {
            return null;
        }

        // Every URL the issue names, from issue_targets where the analysis wrote it (by the URL the issue is on, on its
        // index where it has one); an earlier engine's analysis has only the first, on the issue itself.
        var named = context.HasIssueTargets
            ? "SELECT related_url_id AS target FROM issue_targets WHERE issue = $check AND url_id = $id AND related_url_id <> 0"
            : "SELECT related_url_id AS target FROM issues WHERE issue = $check AND url_id = $id AND related_url_id IS NOT NULL AND related_url_id <> 0";
        var total = context.Scalar($"SELECT COUNT(*) FROM ({named})", parameters);
        List<IssueTarget> targets = [];
        if (listed > 0 && total > 0)
        {
            var (status, _, fetched, outcome, join) = UrlInspectorReader.OtherEnd(context);
            var preferred = (int)PreferredKind(checkId);

            // Where only some of a page's links to a target are at fault, the target's link is the first of those and its
            // links count those alone. A broken fragment's are judged against the target's ids below; until then, the
            // first link with a fragment. Any other check's link is the first of its kind, and every link to it counts.
            var flagged = FlaggedLinks(checkId);
            var flaggedFirst = flagged is null ? string.Empty : $"CASE WHEN {flagged} THEN 0 ELSE 1 END, ";
            var fragmentFirst = checkId == IssueCatalog.BROKEN_FRAGMENT_LINKS.Id ? "CASE WHEN l.fragment IS NULL THEN 1 ELSE 0 END, " : string.Empty;
            var counted = flagged is null ? string.Empty : $" AND {flagged}";
            using var command = context.Command($"""
                SELECT o.id, o.url, {status}, {fetched}, {outcome},
                       (SELECT l.rowid FROM links l WHERE l.from_id = $id AND +l.to_id = o.id
                        ORDER BY {flaggedFirst}CASE WHEN l.kind = {preferred} THEN 0 ELSE 1 END, {fragmentFirst}l.rowid LIMIT 1) AS link,
                       (SELECT COUNT(*) FROM links l WHERE l.from_id = $id AND +l.to_id = o.id{counted}) AS links
                FROM ({named}) t
                JOIN urls o ON o.id = t.target
                {join}
                ORDER BY {TargetOrder(context, checkId)}
                LIMIT {listed.ToString(CultureInfo.InvariantCulture)}
                """, parameters);
            List<(IssueTarget Target, long? Link)> found = [];
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    found.Add((new IssueTarget(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        UrlInspectorReader.Int32OrNull(reader, 2),
                        !reader.IsDBNull(3) && reader.GetBoolean(3),
                        UrlInspectorReader.Known<UrlOutcome>(reader, 4),
                        null, null, null) { Links = (int)Math.Min(reader.GetInt64(6), int.MaxValue) }, reader.IsDBNull(5) ? null : reader.GetInt64(5)));
                }
            }

            if (checkId == IssueCatalog.BROKEN_FRAGMENT_LINKS.Id)
            {
                found = [.. found.Select(entry => MissingSections(context, urlId, entry.Target.Id) is (var link, var count) ? (entry.Target with { Links = count }, link) : entry)];
            }

            var facts = UrlFactsReader.Read(context, [.. found.Select(entry => entry.Target.Id)]);
            var images = new PageImages(context, urlId);
            var imageCheck = IssueCatalog.Find(checkId)?.Category == IssueCategory.Images;
            targets.AddRange(found.Select(entry =>
            {
                var target = (entry.Link is { } link ? WithLink(context, entry.Target, link) : entry.Target) with { Facts = facts.GetValueOrDefault(entry.Target.Id) };
                return target.Kind == LinkKind.Image || imageCheck ? target with { Img = images.Of(target.Url) } : target;
            }));
        }

        return new IssueTargetsOfUrl(urlId, total, targets);
    }

    /// <summary>
    /// The page's links to a target the check flags, as SQL, for the checks where some of them can be fine: a link without
    /// text (outside the head; to the page itself, one not written as a fragment, as its skip and #top links are jumps
    /// within it), a nofollow link (a followed one beside it isn't), and a link written without its scheme (one written
    /// with it isn't). Null for the others.
    /// </summary>
    private static string? FlaggedLinks(string checkId)
    {
        var hyperlink = $"l.kind = {(int)LinkKind.Hyperlink}";
        var samePage = $"(l.to_id = l.from_id OR (l.flags & {(int)LinkFlags.SamePage}) <> 0)";
        if (checkId == IssueCatalog.EMPTY_ANCHOR_TEXT.Id)
        {
            return $"({hyperlink} AND COALESCE(TRIM(l.text), '') = '' AND l.position <> {(int)LinkPosition.Head} AND (l.fragment IS NULL OR NOT {samePage}))";
        }

        if (checkId == IssueCatalog.PROTOCOL_RELATIVE_LINKS.Id)
        {
            return $"((l.flags & {(int)LinkFlags.ProtocolRelative}) <> 0)";
        }

        return checkId == IssueCatalog.INTERNAL_NOFOLLOW_LINKS.Id
            ? $"({hyperlink} AND (l.flags & {(int)LinkFlags.NoFollow}) <> 0 AND NOT {samePage})"
            : null;
    }

    /// <summary>
    /// The page's links to a target whose #fragment the target has no id for, as the check judges them
    /// (<see cref="FragmentRules.Misses"/>): the first, and how many. Null where none does, or the target's ids can't be
    /// read, so the first link with a fragment stands.
    /// </summary>
    private static (long Link, int Count)? MissingSections(GridReadContext context, int pageId, int targetId)
    {
        if (StoredFacts(context, targetId) is not { } facts)
        {
            return null;
        }

        long? first = null;
        var count = 0;
        using var command = context.Command($"""
            SELECT l.rowid, l.fragment FROM links l
            WHERE l.from_id = $id AND +l.to_id = $target AND l.kind = {(int)LinkKind.Hyperlink} AND l.fragment IS NOT NULL
            ORDER BY l.rowid
            """, [new("$id", pageId), new("$target", targetId)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (FragmentRules.Misses(reader.GetString(1), facts.AnchorIds))
            {
                first ??= reader.GetInt64(0);
                count++;
            }
        }

        return first is { } link ? (link, count) : null;
    }

    /// <summary>A page's stored facts, or null when it has none or they can't be read (a damaged blob).</summary>
    private static PageFacts? StoredFacts(GridReadContext context, int urlId)
    {
        using var command = context.Command("SELECT data FROM response_details WHERE url_id = $id", [new("$id", urlId)]);
        if (command.ExecuteScalar() is not byte[] data)
        {
            return null;
        }

        try
        {
            return BlobCodec.DecodeDetails(data).Page;
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The order a page's targets are listed in: by id, the crawl's order of finding them; for <c>image-too-large</c>, the
    /// largest file first (where its redirects end, with a URL summary to say so), so the worst is the one shown.
    /// </summary>
    private static string TargetOrder(GridReadContext context, string checkId)
    {
        if (checkId != IssueCatalog.IMAGE_TOO_LARGE.Id)
        {
            return "o.id";
        }

        var end = context.Mode == GridMode.Summary ? "COALESCE((SELECT f.final_url_id FROM url_summary f WHERE f.url_id = o.id), o.id)" : "o.id";
        return $"(SELECT z.size_bytes FROM responses z WHERE z.url_id = {end}) DESC, o.id";
    }

    /// <summary>The target with what the page's link to it is: its kind, text, position, fragment and flags.</summary>
    private static IssueTarget WithLink(GridReadContext context, IssueTarget target, long link)
    {
        using var command = context.Command("SELECT kind, text, position, fragment, flags FROM links WHERE rowid = $link", [new("$link", link)]);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? target with
            {
                Kind = UrlInspectorReader.Known<LinkKind>(reader, 0),
                Text = reader.IsDBNull(1) ? null : reader.GetString(1),
                Position = UrlInspectorReader.Known<LinkPosition>(reader, 2),
                Fragment = reader.IsDBNull(3) ? null : reader.GetString(3),
                Flags = (LinkFlags)reader.GetInt64(4)
            }
            : target;
    }

    /// <summary>A page's images as it writes them, read from its stored details once, however many of its targets ask.</summary>
    private sealed class PageImages(GridReadContext context, int pageId)
    {
        private IReadOnlyList<ImageFact>? _images;

        public ImageOnPage? Of(string url)
        {
            _images ??= Read();
            return _images.FirstOrDefault(image => image.Url == url) is { } image
                ? new ImageOnPage(image.Width, image.Height, image.Alt, image.HasAlt, image.Alt is { } alt ? RuleText.Length(alt) : null, image.HasSrcset, image.Loading)
                : null;
        }

        // A damaged blob names no image: the targets are still listed.
        private IReadOnlyList<ImageFact> Read() => StoredFacts(context, pageId)?.Images ?? [];
    }

    /// <summary>The kind of link a check is about, where a page can link to its target more than one way.</summary>
    private static LinkKind PreferredKind(string checkId) => IssueCatalog.Find(checkId) switch
    {
        { Category: IssueCategory.Canonicals } => LinkKind.Canonical,
        { Category: IssueCategory.Hreflang } => LinkKind.Hreflang,
        { Category: IssueCategory.Images } => LinkKind.Image,
        var check when check == IssueCatalog.SOCIAL_IMAGE_BROKEN || check == IssueCatalog.SOCIAL_IMAGE_TOO_LARGE || check == IssueCatalog.SOCIAL_IMAGE_TOO_SMALL =>
            LinkKind.SocialImage,
        _ => LinkKind.Hyperlink
    };
}
