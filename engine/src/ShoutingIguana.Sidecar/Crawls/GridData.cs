using System.Text.Json;
using System.Text.Json.Nodes;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Reporting;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Exports;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// The grids' reads of a crawl file, through the engine's readers: the URL grid, its facets and lookup, the inspector
/// with its links and source, and a check's groups and rows. Counts are kept in one cache for every crawl, keyed so a
/// crawl continued or analysed again counts afresh, and each thing is counted once however many requests want it.
/// <list type="bullet">
/// <item>A request given up (the client closed its connection, as a grid scrolled on does) stops its read within
/// milliseconds: the engine interrupts the statement running.</item>
/// <item>At most <see cref="SidecarOptions.GridReads"/> run at once, each on a thread of its own; the rest queue holding
/// no thread, so the API's other calls never wait behind them, and one given up while it queues never runs.</item>
/// </list>
/// </summary>
internal sealed class GridData(CrawlManager crawls, ReadCache cache, ReadSlots slots)
{
    private readonly SemaphoreSlim _reads = slots.Free;

    /// <summary>The columns for the column chooser and the filter bar, a check's own last.</summary>
    public static IReadOnlyList<UrlColumnDto> Columns { get; } =
    [
        .. UrlGridReader.IssueRowColumns.Select(column => new UrlColumnDto(
            column.Id,
            column.Type.ToString().ToLowerInvariant(),
            column.NeedsAnalysis,
            column.Sortable,
            column.Filterable,
            column.Values,
            UrlGridReader.Columns.All(other => other.Id != column.Id),
            column.Title,
            column.Unit,
            column.ValueTitles))
    ];

    /// <summary>The links grid's columns: a link's own, then each end's.</summary>
    public static IReadOnlyList<LinkColumnDto> LinkColumns { get; } =
    [
        .. LinkGridReader.Columns.Select(column => new LinkColumnDto(
            column.Id, column.Type.ToString().ToLowerInvariant(), column.NeedsAnalysis, column.Sortable, column.Filterable, column.Values, column.Title, column.Unit,
            column.ValueTitles))
    ];

    /// <summary>Tests only: called as each read starts, on its own thread, with the token that stops it.</summary>
    internal Action<CancellationToken>? ReadStarting { get; set; }

    public async Task<UrlPageDto> QueryAsync(string id, GridQueryRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Group is not null)
        {
            throw Problems.BadRequest("Only a check's rows take a group: filter by the check, with its group, instead.");
        }

        RefuseSitemap(request);
        RefuseCompareWith(request);

        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        return Page(await ReadAsync(token => UrlGridReader.Query(path, query, cache, token), cancellationToken).ConfigureAwait(false));
    }

    public async Task<FacetsDto> FacetsAsync(string id, FacetsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        var ends = GridRequests.LinkEnds(request?.LinkEnds, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked, LinkEnds = ends, HiddenChecks = request?.HiddenChecks ?? [] };
        return Facets(await ReadAsync(token => UrlGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
    }

    private static FacetsDto Facets(UrlFacets facets) =>
        new(facets.Matching, facets.Total, facets.Summary, facets.Unavailable,
            facets.Facets.ToDictionary(facet => facet.Key, facet => (IReadOnlyList<FacetBucketDto>)
                [.. facet.Value.Select(bucket => new FacetBucketDto(GridRequests.Element(bucket.Value), bucket.Count, GridRequests.Dto(bucket.Filter)) { Min = bucket.Min, Max = bucket.Max })],
                StringComparer.Ordinal));

    public async Task<LinkGridPageDto> LinkGridAsync(string id, GridQueryRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Group is not null)
        {
            throw Problems.BadRequest("Only a check's rows take a group: filter by either end's check, with its group, instead.");
        }

        RefuseSitemap(request);
        RefuseCompareWith(request);
        RefuseLinkEnds(request?.LinkEnds);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var page = await ReadAsync(token => LinkGridReader.Query(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new LinkGridPageDto(page.Columns, new LinkRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable)
        {
            PixelSizes = page.PixelSizes
        };
    }

    /// <summary>
    /// The URLs at one end of the links a links view matches, a page of them: the view's targets, or the pages its links
    /// are on, each once, however many links the view holds.
    /// </summary>
    public async Task<LinkEndPage> LinkEndsAsync(string id, LinkEndsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var end = GridRequests.End(request?.End, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new LinkEndQuery
        {
            End = end!.Value, Filters = filters, Search = request?.Search, Offset = request?.Offset ?? 0, Limit = request?.Limit ?? UrlGridReader.DEFAULT_LIMIT,
            HiddenChecks = request?.HiddenChecks ?? []
        };
        return await ReadAsync(token => LinkGridReader.Ends(path, query, cache, token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A page of the links a links view matches, grouped by target: each target once, with its links, the pages they're on,
    /// their text and positions, and what it is; the most linked from pages first unless the request asks otherwise.
    /// </summary>
    public async Task<LinkGroupPageDto> LinkGroupsAsync(string id, LinkGroupsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var sort = GridRequests.Sorts(request?.Sort, errors);
        if (sort.Count > 1)
        {
            GridRequests.Add(errors, "sort", "The links grouped by target sort by one key.");
        }

        GridRequests.ThrowIfAny(errors);
        var query = new LinkGroupQuery
        {
            Filters = filters, Search = request?.Search, Sort = sort.FirstOrDefault(), Offset = request?.Offset ?? 0, Limit = request?.Limit ?? LinkGroupPage.DEFAULT_LIMIT,
            HiddenChecks = request?.HiddenChecks ?? []
        };
        var page = await ReadAsync(token => LinkGridReader.Groups(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new LinkGroupPageDto(page.Total, page.Links, page.Pages, page.Offset, new LinkGroupSortDto(page.Sort.Column, page.Sort.Descending), page.Rows, page.Summary)
        {
            PixelSizes = page.PixelSizes
        };
    }

    public async Task<FacetsDto> LinkFacetsAsync(string id, FacetsRequest? request, CancellationToken cancellationToken)
    {
        RefuseLinkEnds(request?.LinkEnds);
        var path = crawls.PathOf(id);
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var filters = GridRequests.Filters(request?.Filters, errors);
        var asked = GridRequests.Facets(request?.Facets, errors);
        GridRequests.ThrowIfAny(errors);
        var query = new UrlFacetQuery { Filters = filters, Search = request?.Search, Facets = asked, HiddenChecks = request?.HiddenChecks ?? [] };
        return Facets(await ReadAsync(token => LinkGridReader.Facets(path, query, cache, token), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The crawl's headline numbers, read as a grid is: in one of the read slots, stopped when its request is given up, and
    /// kept with the grids' counts, so they're read once while the crawl stands still (a running crawl's, every 2 s).
    /// Kept, they cost a look at the file's stamp, and that takes no slot: Overview's numbers of one crawl don't queue
    /// behind the link passes of another, on a machine with as few slots as two.
    /// </summary>
    public async Task<CrawlStatsDto> StatsAsync(string id, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var stats = await RunAsync(token => CrawlStatsReader.ReadKept(path, cache, token), cancellationToken).ConfigureAwait(false)
                    ?? await ReadAsync(token => CrawlStatsReader.Read(path, cache, token), cancellationToken).ConfigureAwait(false);
        return CrawlStatsDto.From(stats);
    }

    /// <summary>
    /// Search as it's typed. The crawl's URLs and titles are kept in memory once read, and a search of them passes over
    /// nothing in the file, so it takes no read slot: a keystroke doesn't queue behind the grids' reads, of this crawl or
    /// another. The read that fills them (the first search of a crawl as it stands) queues as any pass over the file does.
    /// </summary>
    public async Task<UrlSearchResults> SearchAsync(string id, SearchRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var limit = request.Limit ?? UrlSearchReader.DEFAULT_LIMIT;
        return await RunAsync(token => UrlSearchReader.SearchKept(path, request.Text!, limit, cache, token), cancellationToken).ConfigureAwait(false)
               ?? await ReadAsync(token => UrlSearchReader.Search(path, request.Text!, limit, cache, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<UrlLookup> LookupAsync(string id, UrlLookupRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["url"] = ["Give a URL, or a path that starts with /."] });
        }

        return await ReadAsync(token => UrlGridReader.Lookup(path, request.Url, token), cancellationToken).ConfigureAwait(false) ?? throw Problems.UrlNotFound();
    }

    public async Task<UrlDetailDto> UrlAsync(string id, UrlRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var url = (GridRequests.UrlId(request.UrlId) is { } urlId
                      ? await ReadAsync(token => UrlInspectorReader.Read(path, urlId, cache, token), cancellationToken).ConfigureAwait(false)
                      : null)
                  ?? throw Problems.UrlNotFound();
        var indexability = url.Indexability;
        return new UrlDetailDto(
            url.Id, url.Url, url.UrlLength, url.IsInternal, url.Type, url.Discovery,
            url.Response is { } response ? UrlResponseDto.From(response) : null,
            url.Redirects,
            new IndexabilityDto(indexability.Indexable, indexability.Reason, indexability.JudgedBy, [.. indexability.Tests.Select(IndexabilityTestDto.From)]),
            url.Content, url.Limits, url.Links, url.LinkTotals, url.Headers, url.StructuredData, url.Hreflang, url.Issues, url.NamedBy, url.Summary,
            new SourceStateDto(url.SourceStored), url.ListedIn);
    }

    public async Task<LinkPageDto> LinksAsync(string id, LinkDirection direction, LinksRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var urlId = GridRequests.UrlId(request.UrlId);
        var query = new LinkQuery
        {
            Kinds = GridRequests.Kinds(request.Kinds),
            Offset = request.Offset ?? 0,
            Limit = request.Limit ?? UrlGridReader.DEFAULT_LIMIT,
            After = request.After
        };
        var page = (urlId is { } known
                       ? await ReadAsync(token => UrlInspectorReader.ReadLinks(path, known, direction, query, cache, token), cancellationToken).ConfigureAwait(false)
                       : null)
                   ?? throw Problems.UrlNotFound();
        return new LinkPageDto([.. page.Rows.Select(LinkRowDto.From)], page.Offset, page.Total, page.Next);
    }

    /// <summary>The images a page shows, each file once, the largest first: the inspector's Images.</summary>
    public async Task<PageImages> ImagesAsync(string id, UrlImagesRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var urlId = GridRequests.UrlId(request.UrlId);
        var limit = request.Limit ?? PageImagesReader.DEFAULT_LIMIT;
        return (urlId is { } known
                   ? await ReadAsync(token => PageImagesReader.Read(path, known, limit, cache, token), cancellationToken).ConfigureAwait(false)
                   : null)
               ?? throw Problems.UrlNotFound();
    }

    /// <summary>Where each of a page of URLs is found: the links to it, the pages they're on, the sitemaps listing it.</summary>
    public async Task<FoundOnDto> FoundOnAsync(string id, FoundOnRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var (urlIds, limit) = FoundOnPage(request);
        if (urlIds.Count == 0)
        {
            return new FoundOnDto([]);
        }

        var urls = await ReadAsync(token => FoundOnReader.ReadFoundOn(path, urlIds, limit, cache, token), cancellationToken).ConfigureAwait(false);
        return new FoundOnDto([.. urls.Select(UrlFoundOnDto.From)]);
    }

    /// <summary>The URLs each of a page of a check's flagged pages names: its broken targets, its canonical.</summary>
    public async Task<IssueTargetsDto> TargetsAsync(string id, string checkId, FoundOnRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var (urlIds, limit) = FoundOnPage(request);
        var urls = urlIds.Count == 0
            ? []
            : await ReadAsync(token => FoundOnReader.ReadTargets(path, checkId, urlIds, limit, cache, token), cancellationToken).ConfigureAwait(false)
              ?? throw Problems.NotAnalyzed();
        if (urls.Count == 0 && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        return new IssueTargetsDto(checkId, [.. urls.Select(IssueTargetsOfUrlDto.From)]);
    }

    /// <summary>The URL ids a request names (an id no URL can have is left out) and how many of each to list.</summary>
    private static (IReadOnlyList<int> UrlIds, int Limit) FoundOnPage(FoundOnRequest request)
    {
        if (request.UrlIds is not { Count: > 0 } ids)
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["urlIds"] = ["Give the URLs' ids."] });
        }

        if (ids.Count > FoundOnReader.MAX_URLS)
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>>
            {
                ["urlIds"] = [$"Give from 1 to {FoundOnReader.MAX_URLS} URL ids."]
            });
        }

        var limit = request.Limit ?? FoundOnReader.DEFAULT_LISTED;
        if (limit is < 0 or > FoundOnReader.MAX_LISTED)
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>>
            {
                ["limit"] = [$"List from 0 to {FoundOnReader.MAX_LISTED} of each URL's."]
            });
        }

        // An id no URL can have names none, as a URL id does elsewhere.
        return ([.. ids.Where(urlId => urlId is >= 1 and <= int.MaxValue).Select(urlId => (int)urlId)], limit);
    }

    /// <summary>
    /// The site's icon as the crawl kept it, or null when it has none. The engine gives out nothing but an image it told
    /// by its bytes as it read the file, with the media type those bytes are.
    /// </summary>
    /// <summary>A Shopify store's crawler access (<see cref="ShopifyAccessReader"/>): null for any other site.</summary>
    public async Task<ShopifyAccessDto?> ShopifyAsync(string id, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var access = await ReadAsync(token => ShopifyAccessReader.Read(path, token), cancellationToken).ConfigureAwait(false);
        return access is null ? null : ShopifyAccessDto.From(access);
    }

    public Task<CrawlFavicon?> FaviconAsync(string id, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        return ReadAsync(token => FaviconReader.Read(path, token), cancellationToken);
    }

    /// <summary>The stored HTML, written as it's unpacked (<see cref="HtmlSourceResult"/>), or why there's none.</summary>
    public async Task<IResult> SourceAsync(string id, UrlRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var source = (GridRequests.UrlId(request.UrlId) is { } urlId
                         ? await ReadAsync(token => UrlInspectorReader.OpenSource(path, urlId, token), cancellationToken).ConfigureAwait(false)
                         : null)
                     ?? throw Problems.UrlNotFound();
        if (source.Missing is { } missing)
        {
            source.Dispose();
            return TypedResults.Json(new HtmlSourceDto(false) { Reason = missing == HtmlSourceMissing.NotStored ? "notStored" : "notPage" },
                SidecarJsonContext.Default.HtmlSourceDto);
        }

        return new HtmlSourceResult(source);
    }

    public async Task<IssueGroupPageDto> GroupsAsync(string id, string checkId, GroupsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var sort = GridRequests.GroupSort(request?.Sort);
        var (offset, limit) = (request?.Offset ?? 0, request?.Limit ?? UrlGridReader.DEFAULT_LIMIT);
        var other = request?.CompareWith is { } with ? crawls.ComparedWith(id, with).Other : null;
        var page = await ReadAsync(token => other is null
                           ? IssueGroupReader.ReadGroups(path, checkId, offset, limit, sort, cache, token)
                           : IssueGroupReader.ReadGroups(path, checkId, offset, limit, sort, other.Item.Path, cache, token),
                       cancellationToken).ConfigureAwait(false)
                   ?? throw Problems.NotAnalyzed();
        if (page.Total is null && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        return new IssueGroupPageDto(page.CheckId, GridRequests.Grouping(page.Grouping), page.Total, page.GroupCount, page.Offset, page.Groups)
        {
            Sort = page.Sort == IssueGroupSort.Size ? "size" : "pages",
            LikelyTemplate = page.LikelyTemplate,
            Compared = other is not null && page.Compared is { } compared
                ? new GroupsComparedDto(other.Item.Id, other.Item.StartedAt, compared.Comparable, compared.FixedGroups)
                : null
        };
    }

    public async Task<UrlPageDto> RowsAsync(string id, string checkId, GridQueryRequest? request, CancellationToken cancellationToken)
    {
        RefuseSitemap(request);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var group = request?.Group is { } key ? new IssueGroupKey(key.Detail, key.RelatedUrlId) : null;
        var other = request?.CompareWith is { } with ? crawls.ComparedWith(id, with).Other : null;
        var rows = await ReadAsync(token => other is null
                           ? IssueGroupReader.ReadRows(path, checkId, query, group, cache, token)
                           : IssueGroupReader.ReadRows(path, checkId, query, group, other.Item.Path, cache, token), cancellationToken).ConfigureAwait(false)
                   ?? throw Problems.NotAnalyzed();
        if (rows.Page is { } page)
        {
            return other is not null && rows.Compared is { } compared
                ? new UrlPageDto(page.Columns, new UrlRowsDto(page.Columns, page.Rows, rows.Changes), page.Offset, page.Matching, page.Total, page.Next, page.Summary,
                    page.Unavailable) { Compared = new ComparedDto(other.Item.Id, other.Item.StartedAt, compared.Comparable) }
                : Page(page);
        }

        // Skipped: no rows and no counts, never 0.
        return IssueCatalog.Find(checkId) is null
            ? throw Problems.CheckNotFound(checkId)
            : new UrlPageDto(["id"], new UrlRowsDto(["id"], []), query.Offset, null, null, null, rows.Summary, []);
    }

    /// <summary>
    /// The comparison with the crawl's previous crawl, or the one <paramref name="with"/> names (the fix loop), read in one of
    /// the read slots and kept with the grids' counts under both files' stamps. A crawl with no previous one has nothing
    /// to compare: <see cref="NoComparisonDto"/>.
    /// </summary>
    public async Task<object> ChangesAsync(string id, string? with, CancellationToken cancellationToken)
    {
        var pair = crawls.ComparedWith(id, with);
        if (pair.Other is not { } other)
        {
            return new NoComparisonDto(id, null);
        }

        var path = pair.Current.Item.Path;
        var comparison = await ReadAsync(token => CrawlComparisonReader.ReadSummary(path, other.Item.Path, cache, token), cancellationToken).ConfigureAwait(false)
                         ?? throw Problems.CrawlNotComparable("That crawl's analysis has gone: analyse it again to compare with it.");
        var item = other.Item;
        var counts = comparison.Totals.Checks;
        return new ChangesDto(
            id,
            new ComparedCrawlDto(item.Id, item.StartedAt, item.FinishedAt, item.Status, item.StartUrl, item.AnalyzedAt, item.PagesAnalyzed, item.CrawledUrlCount,
                pair.Picked ? "picked" : CrawlManager.PREVIOUS),
            comparison.Comparable,
            [.. comparison.Differences.Select(Difference)],
            comparison.PreviousNeedsAnalysis,
            comparison.Score,
            new ComparisonTotalsDto(
                new CheckStateCountsDto(counts[CheckChangeState.Fixed], counts[CheckChangeState.New], counts[CheckChangeState.Fewer], counts[CheckChangeState.More],
                    counts[CheckChangeState.Same], counts[CheckChangeState.NewCheck], counts[CheckChangeState.NotCheckedNow]),
                comparison.Totals.Issues),
            [.. comparison.Checks.Select(CheckChangeDto.From)]);
    }

    /// <summary>
    /// A page of what a check flagged in the other crawl and doesn't flag now, by URL or by cause (the fix loop). A crawl with
    /// no previous crawl has none: no comparison, no total, no rows.
    /// </summary>
    public async Task<FixedPageDto> FixedAsync(string id, string checkId, FixedRequest? request, CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        var level = request?.Level switch
        {
            null or "rows" => ClearedLevel.Rows,
            "groups" => ClearedLevel.Groups,
            _ => (ClearedLevel?)null
        };
        if (level is null)
        {
            errors["level"] = ["Give the level: rows (each URL) or groups (each cause with no URL now)."];
        }

        List<RowChangeState> states = [];
        foreach (var (state, index) in (request?.States ?? []).Select((state, index) => (state, index)))
        {
            switch (state)
            {
                case "fixed":
                    states.Add(RowChangeState.Fixed);
                    break;
                case "gone":
                    states.Add(RowChangeState.Gone);
                    break;
                case "notChecked":
                    states.Add(RowChangeState.NotChecked);
                    break;
                default:
                    errors[$"states[{index}]"] = [$"There's no state called '{state}': there are fixed, gone and notChecked."];
                    break;
            }
        }

        GridRequests.ThrowIfAny(errors);
        var levelName = level == ClearedLevel.Groups ? "groups" : "rows";
        var offset = request?.Offset ?? 0;
        var pair = crawls.ComparedWith(id, request?.CompareWith);
        if (pair.Other is not { } other)
        {
            return IssueCatalog.Find(checkId) is null
                ? throw Problems.CheckNotFound(checkId)
                : new FixedPageDto(checkId, null, levelName, null, offset) { Rows = level == ClearedLevel.Rows ? [] : null, Groups = level == ClearedLevel.Groups ? [] : null };
        }

        var query = new ClearedQuery(level!.Value, states, request?.Group is { } group ? new ClearedGroupKey(group.Detail, group.RelatedUrl) : null, offset,
            request?.Limit ?? UrlGridReader.DEFAULT_LIMIT);
        var path = pair.Current.Item.Path;
        var page = await ReadAsync(token => CrawlComparisonReader.ReadCleared(path, other.Item.Path, checkId, query, cache, token), cancellationToken).ConfigureAwait(false)
                   ?? throw Problems.CrawlNotComparable("That crawl's analysis has gone: analyse it again to compare with it.");
        if (page.Total is null && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        return new FixedPageDto(checkId, new ComparedDto(other.Item.Id, other.Item.StartedAt, page.Comparable), levelName, page.Total, page.Offset)
        {
            Rows = page.Level == ClearedLevel.Rows ? page.Rows : null,
            Groups = page.Level == ClearedLevel.Groups ? page.Groups : null
        };
    }

    /// <summary>A difference as the API writes it: a field, a side and the values only where it has them.</summary>
    private static JsonObject Difference(ComparisonDifference difference)
    {
        JsonObject written = new() { ["code"] = JsonNamingPolicy.CamelCase.ConvertName(difference.Code.ToString()) };
        if (difference.Field is { } field)
        {
            written["field"] = field;
        }

        if (difference.Side is { } side)
        {
            written["side"] = JsonNamingPolicy.CamelCase.ConvertName(side.ToString());
        }

        // An option's values are always given, none (no limit) as null; other differences give theirs where they have them.
        if (difference.Field is not null && difference.Code != ComparisonDifferenceCode.Rules || difference.Then is not null || difference.Now is not null)
        {
            written["then"] = difference.Then?.DeepClone();
            written["now"] = difference.Now?.DeepClone();
        }

        return written;
    }

    /// <summary>The sitemaps the crawl looked for, as the engine lists them.</summary>
    public async Task<SitemapList> SitemapsAsync(string id, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        return await ReadAsync(token => SitemapListReader.Read(path, cache, token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The URL grid for one sitemap: the sitemap's id beside the grid's query.</summary>
    public async Task<UrlPageDto> SitemapUrlsAsync(string id, GridQueryRequest request, CancellationToken cancellationToken)
    {
        if (request.Group is not null)
        {
            throw Problems.BadRequest("Only a check's rows take a group: filter by the check, with its group, instead.");
        }

        RefuseCompareWith(request);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var sitemap = request.SitemapId switch
        {
            null => throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["sitemapId"] = ["Give the sitemap's id."] }),
            < 1 or > int.MaxValue => (int?)null,
            { } known => (int)known
        };
        return Page((sitemap is { } sitemapId
                        ? await ReadAsync(token => UrlGridReader.QuerySitemap(path, sitemapId, query, cache, token), cancellationToken).ConfigureAwait(false)
                        : null)
                    ?? throw Problems.SitemapNotFound());
    }

    public async Task<RobotsOriginPageDto> RobotsAsync(string id, RobotsOriginsRequest? request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var query = new RobotsOriginQuery
        {
            Search = request?.Search, Blocked = request?.Blocked, Offset = request?.Offset ?? 0, Limit = request?.Limit ?? UrlGridReader.DEFAULT_LIMIT
        };
        var page = await ReadAsync(token => RobotsReader.ReadOrigins(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new RobotsOriginPageDto(page.Total, page.Offset, page.Respected, page.UserAgent, page.Blocked, [.. page.Origins.Select(RobotsOriginDto.From)], page.Matching,
            page.Counts);
    }

    public async Task<RobotsFileDto> RobotsFileAsync(string id, RobotsFileRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var origin = Origin(request.Origin);
        var file = await ReadAsync(token => RobotsReader.ReadFile(path, origin, cache, token), cancellationToken).ConfigureAwait(false) ?? throw Problems.OriginNotFound();
        var read = RobotsOriginDto.From(file.Origin);
        return new RobotsFileDto(read.Origin, read.IsInternal, read.State, read.StatusCode, read.FetchedAt, read.Error, read.Bytes, read.Blocked, file.UserAgent, file.Lines,
            file.Unmatched, file.Respected);
    }

    public async Task<RobotsBlockedPage> RobotsBlockedAsync(string id, RobotsBlockedRequest request, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        var origin = Origin(request.Origin);
        RobotsBlockReason? reason = request.Reason switch
        {
            null => null,
            "rule" => RobotsBlockReason.Rule,
            "unreachable" => RobotsBlockReason.Unreachable,
            "unknown" => RobotsBlockReason.Unknown,
            var other => throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>>
            {
                ["reason"] = [$"There's no reason called '{other}': there are rule, unreachable and unknown."]
            })
        };
        // An id no URL could have is one the origin didn't block: no rows, as for any other URL it didn't.
        int? urlId = request.UrlId is { } asked ? asked is < 1 or > int.MaxValue ? 0 : (int)asked : null;
        return await ReadAsync(
                       token => RobotsReader.ReadBlocked(path, origin, request.Line, reason, request.Offset ?? 0, request.Limit ?? UrlGridReader.DEFAULT_LIMIT, cache, token, urlId,
                           request.Conflict),
                       cancellationToken).ConfigureAwait(false)
               ?? throw Problems.OriginNotFound();
    }

    /// <summary>
    /// The report's document (E5), read as a grid is: in one of the read slots, and stopped when its request is given up.
    /// A crawl never analysed has none (<c>not-analyzed</c>).
    /// </summary>
    /// <param name="compareWith">The fix loop: <c>previous</c> (null), <c>none</c> or a crawl's id, which the report says it couldn't be compared with when it can't.</param>
    public async Task<string> ReportAsync(string id, ReportOptions report, string? compareWith, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
        report = crawls.WithComparison(id, report, compareWith);
        return await ReadAsync(token =>
        {
            try
            {
                return ReportExport.Build(path, report, new ExportSettings { Cache = cache }, crawls.AnalysisSettings, token);
            }
            catch (ExportException exception)
            {
                throw new ProblemException(ExportProblems.For(exception), exception);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The origin a robots.txt call names: none is a field error.</summary>
    private static string Origin(string? origin) => string.IsNullOrWhiteSpace(origin)
        ? throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>> { ["origin"] = ["Give the origin, as the list of origins names it."] })
        : origin;

    /// <summary>The ends of some links narrow the URL grids: the links grid is narrowed by its own filters.</summary>
    private static void RefuseLinkEnds(LinkEndsDto? ends)
    {
        if (ends is not null)
        {
            throw Problems.BadRequest("Only the URL grids take linkEnds: the links grid takes the link filters themselves.");
        }
    }

    /// <summary>Only a check's rows are compared with another crawl.</summary>
    private static void RefuseCompareWith(GridQueryRequest? request)
    {
        if (request?.CompareWith is not null)
        {
            throw Problems.BadRequest("Only a check's rows and groups take compareWith.");
        }
    }

    /// <summary>A sitemap's id belongs to the sitemap's own grid.</summary>
    private static void RefuseSitemap(GridQueryRequest? request)
    {
        if (request?.SitemapId is not null)
        {
            throw Problems.BadRequest("Only a sitemap's URLs take a sitemapId: ask /sitemaps/urls for them.");
        }
    }

    private static UrlPageDto Page(UrlPage page) =>
        new(page.Columns, new UrlRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable);

    /// <summary>
    /// A read, once one of the read slots is free, on a thread of its own: SQLite's reads block, and a pool thread blocked
    /// is one the rest of the API waits for. Its refusals come back as the problems the UI keys on.
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<CancellationToken, T> read, CancellationToken cancellationToken)
    {
        await _reads.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(token =>
            {
                ReadStarting?.Invoke(token);
                return read(token);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reads.Release();
        }
    }

    /// <summary>A read on a thread of its own, holding no slot: for one that passes over nothing. Its refusals as <see cref="ReadAsync"/>'s.</summary>
    private static Task<T> RunAsync<T>(Func<CancellationToken, T> read, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Read(() => read(cancellationToken)), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>A read, its refusals as the problems the UI keys on.</summary>
    private static T Read<T>(Func<T> read)
    {
        try
        {
            return EngineErrors.Read(read);
        }
        catch (UrlQueryException exception)
        {
            throw Problems.InvalidQuery(exception.Errors);
        }
        catch (NeedsAnalysisException exception)
        {
            throw Problems.NeedsAnalysis(exception.Reason, exception.Columns);
        }
    }
}
