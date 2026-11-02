using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Analysis.Rules;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Robots;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// One URL in full, for the inspector: how it was found and answered, where its redirects lead, whether it can be
/// indexed and why, what the page says, the issues on it and naming it; and, as reads of their own, its links in and out,
/// and its stored HTML. Each read is one snapshot of the file, held for that read only.
/// </summary>
public static class UrlInspectorReader
{
    /// <summary>The most HTML the source view gets: twice the crawler's default limit on a page.</summary>
    public const int MAX_SOURCE_BYTES = 16 * 1024 * 1024;

    /// <summary>How much of a stored page is unpacked to measure it, so a crafted file can't make the reader unpack forever.</summary>
    public const long MAX_MEASURED_BYTES = 512L * 1024 * 1024;

    // Lenient where browsers and Google are, as the structured data check is.
    private static readonly JsonDocumentOptions JSON_LD = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly IReadOnlyDictionary<string, int> SEVERITY_ORDER = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [nameof(IssueSeverity.Error)] = 0, [nameof(IssueSeverity.Warning)] = 1, [nameof(IssueSeverity.Notice)] = 2
    };

    /// <summary>
    /// The URL with this id in full, or null when the crawl has none. A stored value this engine doesn't know (a newer
    /// engine's kind of link or outcome, a crafted file's) reads as unknown: null.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file isn't a crawl, or was written with another schema.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static UrlDetail? Read(string databasePath, int urlId, ReadCache? cache = null, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache, cancellationToken, context => Read(context, urlId));

    private static UrlDetail? Read(GridReadContext context, int urlId)
    {
        using var command = context.Command($"""
            SELECT u.url, u.is_internal, u.fetch_mode, u.depth, u.state, u.skip_reason, u.source, u.found_on_id, f.url, u.found_via,
                   r.url_id IS NOT NULL, {AnalysisReader.NOT_CHECKED}, r.fetched_at, r.outcome, r.status_code, r.content_type, r.size_bytes,
                   r.body_bytes, r.ttfb_ms, r.response_ms, r.http_version, r.block_reason, r.error, r.word_count,
                   {(context.HasEffectiveIndexability ? "COALESCE(e.indexability_reason, r.indexability_reason)" : "r.indexability_reason")}, d.data,
                   EXISTS (SELECT 1 FROM response_html h WHERE h.url_id = u.id), r.is_html, r.is_nofollow
            FROM urls u
            LEFT JOIN urls f ON f.id = u.found_on_id
            LEFT JOIN responses r ON r.url_id = u.id
            {(context.HasEffectiveIndexability ? "LEFT JOIN effective_indexability e ON e.url_id = u.id" : string.Empty)}
            LEFT JOIN response_details d ON d.url_id = u.id
            WHERE u.id = $id
            """, [new("$id", urlId)]);
        using var row = command.ExecuteReader();
        if (!row.Read())
        {
            return null;
        }

        var url = row.GetString(0);
        var self = Inspected(context, urlId)!;
        var details = row.IsDBNull(25) ? null : Decode((byte[])row.GetValue(25));
        var read = self.Fetched && !row.IsDBNull(23);
        var facts = read ? details?.Page : null;
        var answered = self.Fetched && self.Outcome == FetchOutcome.Success;
        var contentType = row.IsDBNull(15) ? null : row.GetString(15);
        var depth = Int32OrNull(row, 3);
        var crawlReason = self.Fetched ? Known<IndexabilityReason>(row, 24) : null;

        var redirects = Redirects(context, self);
        var (verdict, links) = Verdict(context, self, read, crawlReason);
        var limits = context.Analysis?.Settings is { } settings ? Limits(settings, fromAnalysis: true) : Limits(new AnalysisSettings(), fromAnalysis: false);
        return new UrlDetail
        {
            Id = urlId,
            Url = url,
            IsInternal = self.IsInternal,
            Type = answered && contentType is not null ? ResourceTypes.OfContentType(contentType) : ResourceTypes.OfUrl(url),
            Discovery = new UrlDiscovery(
                Known<DiscoverySource>(row, 6),
                depth is null or >= Crawling.CrawlDepth.UNLINKED or < 0 ? null : depth,
                row.IsDBNull(7) || row.IsDBNull(8) || Int32OrNull(row, 7) is not { } foundOn ? null : new UrlRef(foundOn, row.GetString(8)),
                Known<LinkKind>(row, 9),
                Known<FetchMode>(row, 2),
                Known<UrlState>(row, 4),
                Known<UrlState>(row, 4) == UrlState.Skipped ? Known<SkipReason>(row, 5) : null),
            Response = self.HasResponse ? Response(row, self) : null,
            Redirects = redirects,
            Indexability = Checklist(context, self, details, facts, read, row, verdict),
            Content = facts is null ? null : Content(facts),
            Limits = limits,
            Links = links,
            Headers = details?.Headers ?? [],
            StructuredData = facts is null ? [] : [.. facts.JsonLd.Select(StructuredData)],
            Hreflang = facts is null ? [] : [.. facts.Hreflangs.Select(entry => Hreflang(context, entry))],
            Issues = context.Analysis is null ? null : Issues(context, urlId),
            NamedBy = context.Analysis is null ? null : NamedBy(context, urlId),
            Summary = context.Summary,
            SourceStored = row.GetBoolean(26)
        };
    }

    /// <summary>
    /// One page of the links to or from a URL, in the order they were found, a page's links to itself left out; null when
    /// the crawl has no such URL. Each row names the other end, with its status.
    /// </summary>
    /// <exception cref="UrlQueryException">The paging is out of range, or the cursor isn't this list's.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static LinkPage? ReadLinks(string databasePath, int urlId, LinkDirection direction, LinkQuery query, ReadCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        UrlQueryCompiler.ValidatePaging(query.Offset, query.Limit, query.After, errors);
        if (query.Kinds.Count > Enum.GetValues<LinkKind>().Length)
        {
            errors["kinds"] = ["Name each kind of link once."];
        }

        UrlGridReader.ThrowIfInvalid(errors);
        return GridReadContext.Read(databasePath, cache, cancellationToken, context => ReadLinks(context, urlId, direction, query));
    }

    private static LinkPage? ReadLinks(GridReadContext context, int urlId, LinkDirection direction, LinkQuery query)
    {
        if (Inspected(context, urlId) is not { } self)
        {
            return null;
        }

        var (near, far) = direction == LinkDirection.Inlinks ? ("to_id", "from_id") : ("from_id", "to_id");
        var kinds = query.Kinds.Distinct().Order().ToList();
        var ofKinds = kinds.Count == 0 ? string.Empty : $" AND l.kind IN ({string.Join(", ", kinds.Select(kind => (int)kind))})";
        var where = $"l.{near} = $id AND l.{far} <> $id{ofKinds}";
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        var hash = $"{direction}|{urlId}|{ofKinds}";
        var total = context.Count($"links|{hash}", () => Count(context, direction, urlId, kinds, ofKinds));

        // Where the page starts: after a cursor's link; at an offset found on the index alone, when every kind is listed;
        // or, for some kinds only, at an offset read through the links, from the far end past half way.
        string from;
        long start = 0, skip = 0;
        var reverse = false;
        long take;
        if (query.After is { } cursor)
        {
            // A link stays as the crawl stored it: its place is its row id.
            start = GridCursor.Decode(cursor, Hash(hash), _ => [], "after").Id;
            from = " AND l.rowid > $start";
            take = query.Limit + 1;
        }
        else
        {
            take = Math.Min(query.Limit, Math.Max(0, total - query.Offset));
            if (kinds.Count == 0)
            {
                start = query.Offset == 0 ? 0 : Locate(context, near, urlId, query.Offset) ?? long.MaxValue;
                from = " AND l.rowid >= $start";
            }
            else
            {
                reverse = query.Offset > total / 2;
                skip = reverse ? total - query.Offset - take : query.Offset;
                from = string.Empty;
            }
        }

        var (status, indexable, fetched, join) = OtherEnd(context);
        var sourceNofollow = direction == LinkDirection.Inlinks ? "COALESCE(src.is_nofollow, 0)" : "$selfNofollow";

        List<(LinkRow Link, long RowId)> rows = [];
        if (take > 0)
        {
            using var command = context.Command($"""
                SELECT o.id, o.url, l.kind, l.text, l.position, l.flags, l.fragment, {status}, {indexable}, {fetched}, {sourceNofollow}, l.rowid
                FROM links l
                JOIN urls o ON o.id = l.{far}
                {join}
                {(direction == LinkDirection.Inlinks ? "LEFT JOIN responses src ON src.url_id = l.from_id" : string.Empty)}
                WHERE {where}{from}
                ORDER BY l.rowid {(reverse ? "DESC" : "ASC")}
                LIMIT {take.ToString(CultureInfo.InvariantCulture)} OFFSET {skip.ToString(CultureInfo.InvariantCulture)}
                """, parameters.Concat([new("$start", start), new("$selfNofollow", SelfNofollow(context, urlId) ? 1 : 0)]));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kind = Known<LinkKind>(reader, 2);
                var flags = (LinkFlags)reader.GetInt64(5);
                rows.Add((new LinkRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    kind,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Known<LinkPosition>(reader, 4),
                    flags,
                    kind == LinkKind.Hyperlink ? !flags.HasFlag(LinkFlags.NoFollow) && reader.GetInt64(10) == 0 : null,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    Int32OrNull(reader, 7),
                    reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                    !reader.IsDBNull(9) && reader.GetBoolean(9)), reader.GetInt64(11)));
            }
        }

        if (reverse)
        {
            rows.Reverse();
        }

        var more = query.After is null ? query.Offset + rows.Count < total : rows.Count > query.Limit;
        if (query.After is not null && more)
        {
            rows = rows[..query.Limit];
        }

        return new LinkPage([.. rows.Select(row => row.Link)], query.After is null ? query.Offset : null, total,
            more && rows.Count > 0 ? GridCursor.Encode(Hash(hash), [], rows[^1].RowId) : null);
    }

    /// <summary>
    /// The row id of the link at <paramref name="position"/> among a URL's links of every kind, its links to itself left
    /// out: found on the index of its end alone, so reaching the middle of a home page's two million inlinks reads no
    /// table rows. Its links to itself, few and found through the other index, are skipped over. Null past the end.
    /// </summary>
    private static long? Locate(GridReadContext context, string near, int urlId, long position)
    {
        List<long> own = [];
        using (var command = context.Command("SELECT l.rowid FROM links l WHERE l.from_id = $id AND +l.to_id = $id ORDER BY l.rowid", [new("$id", urlId)]))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                own.Add(reader.GetInt64(0));
            }
        }

        // The index entry at the offset, moved on by the URL's own links at or before it, until it's the one wanted. The
        // offset only grows, and never past the link wanted, so the entry it stops at is never one of the URL's own.
        var skip = position;
        for (var attempt = 0; attempt <= own.Count + 1; attempt++)
        {
            using var command = context.Command($"SELECT l.rowid FROM links l WHERE l.{near} = $id ORDER BY l.rowid LIMIT 1 OFFSET $skip",
                [new("$id", urlId), new("$skip", skip)]);
            if (command.ExecuteScalar() is not long rowId)
            {
                return null;
            }

            var before = own.Count(id => id <= rowId);
            if (skip - before == position)
            {
                return rowId;
            }

            skip = position + before;
        }

        return null;
    }

    /// <summary>
    /// The other end of a link as the grids show it: its status, whether it can be indexed, whether it was fetched; from
    /// the URL summary, or without one from the crawl's tables, as the URL grid's columns read them.
    /// </summary>
    private static (string Status, string Indexable, string Fetched, string Join) OtherEnd(GridReadContext context)
    {
        if (context.Mode == GridMode.Summary)
        {
            return ("os.status_code", "os.indexable", "(os.status_code IS NOT NULL OR os.indexability_reason IS NOT NULL)", "LEFT JOIN url_summary os ON os.url_id = o.id");
        }

        var notChecked = AnalysisReader.NOT_CHECKED.Replace("r.", "orr.", StringComparison.Ordinal);
        var fetched = $"(orr.url_id IS NOT NULL AND NOT {notChecked})";
        var reason = context.HasEffectiveIndexability ? "COALESCE(oe.indexability_reason, orr.indexability_reason)" : "orr.indexability_reason";
        var unread = $"(orr.outcome = {(int)FetchOutcome.Success} AND orr.is_html = 1 AND {reason} = 0 AND orr.word_count IS NULL)";
        var join = "LEFT JOIN responses orr ON orr.url_id = o.id" +
                   (context.HasEffectiveIndexability ? " LEFT JOIN effective_indexability oe ON oe.url_id = o.id" : string.Empty);
        return ($"CASE WHEN {fetched} AND orr.status_code > 0 THEN orr.status_code END", $"CASE WHEN {fetched} AND NOT {unread} THEN {reason} = 0 END", fetched, join);
    }

    /// <summary>
    /// The URL's stored HTML, cut at <see cref="MAX_SOURCE_BYTES"/>, or why it has none; null when the crawl has no such
    /// URL. It's read whole, into one string: the source view reads it a piece at a time (<see cref="OpenSource"/>).
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static HtmlSource? ReadSource(string databasePath, int urlId, CancellationToken cancellationToken = default)
    {
        using var source = OpenSource(databasePath, urlId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (source.Missing is { } missing)
        {
            return new HtmlSource(null, null, false, missing);
        }

        var text = new StringBuilder();
        var buffer = new char[32 * 1024];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            text.Append(buffer, 0, count);
        }

        return new HtmlSource(text.ToString(), source.Bytes, source.Truncated, null);
    }

    /// <summary>
    /// The URL's stored HTML to read a piece at a time, so a large page is never held whole, or why it has none; null when
    /// the crawl has no such URL. The page is read from the file packed, as stored, and the file let go before this
    /// returns: unpacking it holds nothing open.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled, now or while the HTML is read.</exception>
    public static HtmlSourceReader? OpenSource(string databasePath, int urlId, CancellationToken cancellationToken = default) =>
        GridReadContext.Read(databasePath, cache: null, cancellationToken, context =>
        {
            if (Inspected(context, urlId) is null)
            {
                return null;
            }

            using var command = context.Command("SELECT html FROM response_html WHERE url_id = $id", [new("$id", urlId)]);
            return command.ExecuteScalar() is byte[] html
                ? new HtmlSourceReader(html, cancellationToken)
                : new HtmlSourceReader(context.Options.StoreHtml ? HtmlSourceMissing.NotPage : HtmlSourceMissing.NotStored);
        });

    /// <summary>The facts the inspector judges a URL by, as the analysis keeps them for every URL; null when there's no such URL.</summary>
    private static InspectedUrl? Inspected(GridReadContext context, int urlId)
    {
        using var command = context.Command($"""
            SELECT u.id, u.url, u.is_internal, r.url_id IS NOT NULL, {AnalysisReader.NOT_CHECKED}, r.outcome, r.status_code, r.is_html,
                   r.word_count IS NOT NULL AND instr(u.url, '/cdn-cgi/') = 0, r.redirect_url_id
            FROM urls u LEFT JOIN responses r ON r.url_id = u.id
            WHERE u.id = $id
            """, [new("$id", urlId)]);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var hasResponse = reader.GetBoolean(3);
        return new InspectedUrl(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            hasResponse,
            hasResponse && reader.GetBoolean(4),
            hasResponse ? Known<FetchOutcome>(reader, 5) : null,
            Int32OrNull(reader, 6),
            hasResponse && reader.GetBoolean(7),
            hasResponse && !reader.IsDBNull(8) && reader.GetBoolean(8),
            Int32OrNull(reader, 9) ?? 0);
    }

    /// <summary>A stored enum's value, or null for one this engine doesn't know: a newer engine's, or a crafted file's.</summary>
    private static TEnum? Known<TEnum>(SqliteDataReader reader, int ordinal) where TEnum : struct, Enum
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt64(ordinal));
        return Enum.IsDefined(value) ? value : null;
    }

    /// <summary>A stored number as an int, or null when there's none or it doesn't fit one (a crafted file's infinity).</summary>
    private static int? Int32OrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) || reader.GetInt64(ordinal) is < int.MinValue or > int.MaxValue ? null : (int)reader.GetInt64(ordinal);

    /// <summary>
    /// Where a URL moves on to, as the analysis follows it: an HTTP redirect's Location, or, for a page the analysis audits,
    /// an instant (0-second) meta refresh, which search engines treat as a redirect.
    /// </summary>
    private static (InspectedUrl Target, RedirectVia Via)? NextHop(GridReadContext context, InspectedUrl url)
    {
        if (url is { Fetched: true, Outcome: FetchOutcome.Redirect, RedirectTargetId: > 0 })
        {
            return Inspected(context, url.RedirectTargetId) is { } target ? (target, RedirectVia.Http) : null;
        }

        if (!url.IsInternal || !url.IsPage)
        {
            return null;
        }

        using var command = context.Command($"""
            SELECT l.to_id, d.data FROM links l JOIN response_details d ON d.url_id = l.from_id
            WHERE l.from_id = $id AND l.kind = {(int)LinkKind.MetaRefresh} AND l.to_id <> l.from_id
            ORDER BY l.rowid LIMIT 1
            """, [new("$id", url.Id)]);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || Decode((byte[])reader.GetValue(1))?.Page?.MetaRefresh is not { DelaySeconds: 0 })
        {
            return null;
        }

        return Inspected(context, reader.GetInt32(0)) is { } refreshed ? (refreshed, RedirectVia.MetaRefresh) : null;
    }

    /// <summary>
    /// The URL's redirects hop by hop, as the analysis's trace follows them: until a URL doesn't move on, comes back on
    /// itself, or after as many hops as browsers follow.
    /// </summary>
    private static RedirectChain Redirects(GridReadContext context, InspectedUrl start)
    {
        List<RedirectHop> hops = [];
        HashSet<int> visited = [start.Id];
        var current = start;
        var loop = false;
        while (hops.Count < UrlTable.MAX_REDIRECT_HOPS && NextHop(context, current) is { } next)
        {
            hops.Add(new RedirectHop(next.Target.Id, next.Target.Url, next.Target.Status, next.Target.Shown, next.Via));
            current = next.Target;
            if (!visited.Add(current.Id))
            {
                loop = true;
                break;
            }
        }

        var tooLong = !loop && hops.Count == UrlTable.MAX_REDIRECT_HOPS && NextHop(context, current) is not null;
        return new RedirectChain(hops, new UrlTarget(current.Id, current.Url, current.Status), loop, tooLong);
    }

    /// <summary>
    /// The URL doesn't lead to a working response: its redirects loop, stop at a redirect (without a usable Location, or
    /// after too many hops), or end at a URL that fails. As the analysis's <c>UrlTable.IsBrokenThroughRedirects</c>.
    /// </summary>
    private static bool IsBrokenThroughRedirects(GridReadContext context, InspectedUrl url)
    {
        var chain = Redirects(context, url);
        var end = chain.Hops.Count == 0 ? url : Inspected(context, chain.Final.Id)!;
        return chain.Loop || chain.TooLong || end.IsBroken || end is { Fetched: true, Outcome: FetchOutcome.Redirect };
    }

    /// <summary>The verdict and the link counts: the analysis's, from the URL summary; without one, the crawl's own judgement.</summary>
    private static (IndexabilityChecklist Verdict, UrlLinkCounts? Links) Verdict(GridReadContext context, InspectedUrl self, bool read, IndexabilityReason? crawlReason)
    {
        if (context.Mode == GridMode.Summary)
        {
            using var command = context.Command("""
                SELECT indexable, indexability_reason, inlinks, followed_inlinks, nofollow_inlinks, linking_pages, outlinks, in_sitemap
                FROM url_summary WHERE url_id = $id
                """, [new("$id", self.Id)]);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return (new IndexabilityChecklist(
                        reader.IsDBNull(0) ? null : reader.GetBoolean(0),
                        Known<IndexabilityReason>(reader, 1),
                        IndexabilityJudge.Analysis, []),
                    new UrlLinkCounts(Int32OrNull(reader, 2) ?? 0, Int32OrNull(reader, 3) ?? 0, Int32OrNull(reader, 4) ?? 0, Int32OrNull(reader, 5) ?? 0,
                        Int32OrNull(reader, 6), reader.IsDBNull(7) ? null : reader.GetBoolean(7)));
            }
        }

        // As the summary would judge it: a page answered but only status-checked is unknown unless its response rules it out.
        var unread = self is { Fetched: true, Outcome: FetchOutcome.Success, IsHtml: true } && crawlReason == IndexabilityReason.None && !read;
        var judged = self.Fetched && !unread;
        return (new IndexabilityChecklist(judged ? crawlReason == IndexabilityReason.None : null, judged ? crawlReason : null, IndexabilityJudge.Crawl, []), null);
    }

    /// <summary>"Indexable, and why": the verdict with each test, in the order they decide it.</summary>
    private static IndexabilityChecklist Checklist(GridReadContext context, InspectedUrl self, ResponseDetails? details, PageFacts? facts, bool read,
        SqliteDataReader row, IndexabilityChecklist verdict)
    {
        var token = context.RobotsToken;
        var refused = self is { Fetched: true, Outcome: FetchOutcome.RobotsDisallowed };
        var error = row.IsDBNull(22) ? null : row.GetString(22);

        IndexabilityTest answered;
        if (!self.Fetched || refused)
        {
            answered = new IndexabilityTest(IndexabilityTestKind.Answered, TestOutcome.NotChecked);
        }
        else if (self.Outcome == FetchOutcome.Success)
        {
            var refresh = self.IsInternal && self.IsPage && NextHop(context, self) is { Via: RedirectVia.MetaRefresh } hop ? hop.Target : null;
            answered = new IndexabilityTest(IndexabilityTestKind.Answered, refresh is null ? TestOutcome.Pass : TestOutcome.Fail)
            {
                StatusCode = self.Status,
                MetaRefresh = refresh is null ? null : new UrlTarget(refresh.Id, refresh.Url, refresh.Status)
            };
        }
        else
        {
            answered = new IndexabilityTest(IndexabilityTestKind.Answered, TestOutcome.Fail) { StatusCode = self.Status };
        }

        var robotsTxt = !self.Fetched
            ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.NotChecked)
            : refused
                ? new IndexabilityTest(IndexabilityTestKind.RobotsTxt, TestOutcome.Fail)
                {
                    Unreachable = error?.StartsWith(RobotsTxt.UNREACHABLE_ERROR, StringComparison.Ordinal) == true
                }
                : new IndexabilityTest(IndexabilityTestKind.RobotsTxt, context.Options.RespectRobotsTxt ? TestOutcome.Pass : TestOutcome.NotChecked);

        // A noindex or a canonical is only read from a page that answered; one that didn't makes them moot.
        var page = self is { Fetched: true, Outcome: FetchOutcome.Success };
        IndexabilityTest noindex;
        IndexabilityTest canonical;
        if (!page || answered.Outcome == TestOutcome.Fail)
        {
            noindex = new IndexabilityTest(IndexabilityTestKind.Noindex, TestOutcome.NotChecked);
            canonical = new IndexabilityTest(IndexabilityTestKind.Canonical, TestOutcome.NotChecked);
        }
        else
        {
            var header = details is null
                ? (bool?)null
                : RobotsMetaParser.Parse([], details.Headers.Where(header => header.Name == "x-robots-tag").Select(header => header.Value), token).NoIndex;
            bool? meta = null;
            if (read && facts is not null)
            {
                var tags = facts.MetaTags.Where(tag => RobotsMetaParser.AppliesToMetaName(tag.Name, token)).Select(tag => tag.Content);

                // The facts keep 200 meta tags: a noindex beyond them is still in the page's directives, and not the header's.
                meta = RobotsMetaParser.Parse(tags, [], token).NoIndex || (facts.Robots.NoIndex && header != true);
            }

            noindex = new IndexabilityTest(IndexabilityTestKind.Noindex,
                meta == true || header == true ? TestOutcome.Fail : read ? TestOutcome.Pass : TestOutcome.NotChecked)
            {
                Meta = meta,
                Header = header
            };
            canonical = Canonical(context, self, details?.Page, read);
        }

        return verdict with { Tests = [answered, robotsTxt, noindex, canonical] };
    }

    /// <summary>
    /// The canonical a search engine honours (the first in the head or a Link header) and whether it keeps the URL out: not
    /// when it names the URL itself, nor when it leads back to it through redirects or leads nowhere, which the analysis
    /// ignores as Google does.
    /// </summary>
    private static IndexabilityTest Canonical(GridReadContext context, InspectedUrl self, PageFacts? facts, bool read)
    {
        if (facts?.Canonicals.FirstOrDefault(fact => fact.Url is not null && (fact.InHead || fact.FromHttpHeader)) is not { Url: { } target })
        {
            // Without the HTML, only a canonical in a Link header would have been seen.
            return new IndexabilityTest(IndexabilityTestKind.Canonical, read ? TestOutcome.Pass : TestOutcome.NotChecked);
        }

        if (target == self.Url)
        {
            return new IndexabilityTest(IndexabilityTestKind.Canonical, TestOutcome.Pass) { Canonical = new UrlTarget(self.Id, self.Url, self.Status) };
        }

        using var command = context.Command("SELECT id FROM urls WHERE url = $url", [new("$url", target)]);
        if (command.ExecuteScalar() is not long id || Inspected(context, (int)id) is not { } named)
        {
            return new IndexabilityTest(IndexabilityTestKind.Canonical, TestOutcome.Fail);
        }

        var ignored = Redirects(context, named).Final.Id == self.Id || IsBrokenThroughRedirects(context, named);
        return new IndexabilityTest(IndexabilityTestKind.Canonical, ignored ? TestOutcome.Pass : TestOutcome.Fail)
        {
            Canonical = new UrlTarget(named.Id, named.Url, named.Status)
        };
    }

    private static UrlResponse Response(SqliteDataReader row, InspectedUrl self)
    {
        // robots.txt refusing a URL is stored without a request, as is one not checked: neither has times to show.
        var requested = self.Fetched && self.Outcome != FetchOutcome.RobotsDisallowed;
        return new UrlResponse(
            self.Fetched,
            self.Shown,
            self.Status,
            row.IsDBNull(15) ? null : row.GetString(15),
            row.IsDBNull(20) ? null : row.GetString(20),
            DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(row.GetInt64(12), DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())),
            requested ? Int32OrNull(row, 18) : null,
            requested ? Int32OrNull(row, 19) : null,
            row.IsDBNull(16) ? null : row.GetInt64(16),
            row.IsDBNull(17) ? null : row.GetInt64(17),
            row.IsDBNull(21) ? null : row.GetString(21),
            row.IsDBNull(22) ? null : row.GetString(22));
    }

    private static PageContent Content(PageFacts facts)
    {
        var headings = new int[6];
        foreach (var heading in facts.Headings.Where(heading => heading.Level is >= 1 and <= 6))
        {
            headings[heading.Level - 1]++;
        }

        return new PageContent(
            [.. facts.Titles.Select(Measured)],
            [.. facts.MetaDescriptions.Select(Measured)],
            [.. facts.Headings.Where(heading => heading.Level == 1).Select(heading => Measured(heading.Text))],
            headings,
            facts.WordCount,
            facts.Language,
            facts.DeclaredCharset,
            facts.Robots.Directives,
            facts.MetaRefresh is { } refresh ? new MetaRefreshInfo(refresh.DelaySeconds, refresh.Url) : null,
            facts.Images.Count,
            facts.Images.Count(image => !image.HasAlt));
    }

    private static MeasuredText Measured(string text) => new(text, DisplayWidth.Measure(text).Width);

    private static ContentLimits Limits(AnalysisSettings settings, bool fromAnalysis) => new(
        settings.TitleMinLength, settings.TitleMaxLength, settings.DescriptionMinLength, settings.DescriptionMaxLength, settings.MinWordCount,
        settings.SlowResponseMs, settings.LargePageBytes, fromAnalysis);

    private static StructuredDataBlock StructuredData(string block)
    {
        // A block at the parser's cap was cut short, so it can't be judged, as the structured data check doesn't.
        if (block.Length >= PageWalker.MAX_JSON_LD_CHARS)
        {
            return new StructuredDataBlock(false, [], block, null, true);
        }

        try
        {
            using var document = JsonDocument.Parse(block, JSON_LD);
            List<string> types = [];
            Types(document.RootElement, types, depth: 0);
            return new StructuredDataBlock(true, types, block, null, false);
        }
        catch (JsonException exception)
        {
            return new StructuredDataBlock(false, [], block, exception.Message, false);
        }
        catch (InvalidOperationException)
        {
            return new StructuredDataBlock(false, [], block, "A property name escapes an invalid character, such as half of a surrogate pair", false);
        }
    }

    /// <summary>The <c>@type</c>s of a JSON-LD block: its own, an array's items', and those in its <c>@graph</c>.</summary>
    private static void Types(JsonElement element, List<string> types, int depth)
    {
        if (depth > 2)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Types(item, types, depth + 1);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("@type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
            {
                types.Add(type.GetString()!);
            }
            else if (type.ValueKind == JsonValueKind.Array)
            {
                types.AddRange(type.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
            }
        }

        if (element.TryGetProperty("@graph", out var graph))
        {
            Types(graph, types, depth + 1);
        }
    }

    private static HreflangEntry Hreflang(GridReadContext context, HreflangFact entry)
    {
        UrlTarget? target = null;
        if (entry.Url is { } url)
        {
            using var command = context.Command("SELECT id FROM urls WHERE url = $url", [new("$url", url)]);
            if (command.ExecuteScalar() is long id && Inspected(context, (int)id) is { } found)
            {
                target = new UrlTarget(found.Id, found.Url, found.Status);
            }
        }

        return new HreflangEntry(entry.Language, entry.Href, entry.Url, target, entry.InHead, entry.FromHttpHeader);
    }

    /// <summary>The checks that flagged the URL, errors first, with the other URL involved.</summary>
    private static List<UrlIssue> Issues(GridReadContext context, int urlId)
    {
        using var command = context.Command("""
            SELECT i.issue, t.severity, i.detail, i.related_url_id, ru.url
            FROM issues i JOIN issue_types t ON t.id = i.issue LEFT JOIN urls ru ON ru.id = i.related_url_id
            WHERE i.url_id = $id
            """, [new("$id", urlId)]);
        using var reader = command.ExecuteReader();
        List<(UrlIssue Issue, int Order)> issues = [];
        while (reader.Read())
        {
            var severity = reader.GetString(1);
            issues.Add((new UrlIssue(
                reader.GetString(0),
                Enum.TryParse<IssueSeverity>(severity, ignoreCase: false, out var known) && Enum.IsDefined(known) ? known : null,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) || reader.IsDBNull(4) ? null : new UrlRef(reader.GetInt32(3), reader.GetString(4))), SEVERITY_ORDER.GetValueOrDefault(severity, 3)));
        }

        return [.. issues.OrderBy(issue => issue.Order).ThenBy(issue => issue.Issue.CheckId, StringComparer.Ordinal).Select(issue => issue.Issue)];
    }

    /// <summary>The issues on other URLs that name this one as the other URL involved, by check, most first.</summary>
    private static NamedByIssues NamedBy(GridReadContext context, int urlId)
    {
        using var command = context.Command("""
            SELECT issue, COUNT(*) FROM issues WHERE related_url_id = $id AND url_id <> $id GROUP BY issue ORDER BY 2 DESC, 1
            """, [new("$id", urlId)]);
        using var reader = command.ExecuteReader();
        List<CheckCount> checks = [];
        while (reader.Read())
        {
            checks.Add(new CheckCount(reader.GetString(0), reader.GetInt64(1)));
        }

        return new NamedByIssues(checks.Sum(check => check.Count), checks);
    }

    /// <summary>
    /// The links of the asked kinds between the URL and others. The index on the URL's end counts its links of every kind
    /// without reading them; its own links to itself, which that count holds, are few, and found through the other index.
    /// Hyperlinks to it are the URL summary's inlinks, which count those on the pages audited, and the few on the pages
    /// the analysis doesn't audit (Cloudflare's /cdn-cgi/ ones), which only parsed pages have: so a home page's two
    /// million are counted without reading each link for its kind.
    /// </summary>
    private static long Count(GridReadContext context, LinkDirection direction, int urlId, IReadOnlyList<LinkKind> kinds, string ofKinds)
    {
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        if (direction == LinkDirection.Inlinks && kinds is [LinkKind.Hyperlink] && context.Mode == GridMode.Summary)
        {
            var audited = context.Scalar("SELECT COALESCE((SELECT inlinks FROM url_summary WHERE url_id = $id), 0)", parameters);
            var unaudited = context.Cached("unaudited pages", () => UnauditedPages(context));
            return unaudited.Json is null || unaudited.Count == 0
                ? audited
                : audited + context.Scalar($"""
                    SELECT COUNT(*) FROM links l
                    WHERE l.from_id IN (SELECT value FROM json_each($pages)) AND +l.to_id = $id AND l.from_id <> $id AND l.kind = {(int)LinkKind.Hyperlink}
                    """, parameters.Append(new("$pages", unaudited.Json)));
        }

        var near = direction == LinkDirection.Inlinks ? "to_id" : "from_id";
        var all = context.Scalar($"SELECT COUNT(*) FROM links l WHERE l.{near} = $id{ofKinds}", parameters);

        // Through the page's own links, never the other index: a home page has a million links to it, and few of its own.
        var own = context.Scalar($"SELECT COUNT(*) FROM links l WHERE l.from_id = $id AND +l.to_id = $id{ofKinds}", parameters);
        return all - own;
    }

    /// <summary>Parsed pages the analysis doesn't audit (Cloudflare's /cdn-cgi/ ones): their links count in no URL's inlinks.</summary>
    private static IdSet UnauditedPages(GridReadContext context)
    {
        List<long> pages = [];
        using var command = context.Command("""
            SELECT u.id FROM urls u JOIN responses r ON r.url_id = u.id WHERE instr(u.url, '/cdn-cgi/') > 0 AND r.word_count IS NOT NULL
            """, []);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            pages.Add(reader.GetInt64(0));
        }

        return new IdSet($"[{string.Join(",", pages)}]", pages.Count);
    }

    /// <summary>The URL's own page says nofollow, so none of its hyperlinks are followed.</summary>
    private static bool SelfNofollow(GridReadContext context, int urlId) =>
        context.Scalar("SELECT COALESCE((SELECT is_nofollow FROM responses WHERE url_id = $id), 0)", [new("$id", urlId)]) != 0;

    /// <summary>The list a links cursor belongs to (direction, URL and kinds), hashed, so the cursor carries none of it.</summary>
    private static string Hash(string list) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("links:" + list)))[..16];

    private static ResponseDetails? Decode(byte[] data)
    {
        try
        {
            return BlobCodec.DecodeDetails(data);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return null;
        }
    }
}
