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
        RefuseLinkEnds(request?.LinkEnds);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var page = await ReadAsync(token => LinkGridReader.Query(path, query, cache, token), cancellationToken).ConfigureAwait(false);
        return new LinkGridPageDto(page.Columns, new LinkRowsDto(page.Columns, page.Rows), page.Offset, page.Matching, page.Total, page.Next, page.Summary, page.Unavailable);
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
        var page = await ReadAsync(token => IssueGroupReader.ReadGroups(path, checkId, request?.Offset ?? 0, request?.Limit ?? UrlGridReader.DEFAULT_LIMIT, sort, cache, token),
                       cancellationToken).ConfigureAwait(false)
                   ?? throw Problems.NotAnalyzed();
        if (page.Total is null && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        return new IssueGroupPageDto(page.CheckId, GridRequests.Grouping(page.Grouping), page.Total, page.GroupCount, page.Offset, page.Groups)
        {
            Sort = page.Sort == IssueGroupSort.Size ? "size" : "pages",
            LikelyTemplate = page.LikelyTemplate
        };
    }

    public async Task<UrlPageDto> RowsAsync(string id, string checkId, GridQueryRequest? request, CancellationToken cancellationToken)
    {
        RefuseSitemap(request);
        var path = crawls.PathOf(id);
        var query = GridRequests.Query(request);
        var group = request?.Group is { } key ? new IssueGroupKey(key.Detail, key.RelatedUrlId) : null;
        var rows = await ReadAsync(token => IssueGroupReader.ReadRows(path, checkId, query, group, cache, token), cancellationToken).ConfigureAwait(false)
                   ?? throw Problems.NotAnalyzed();
        if (rows.Page is { } page)
        {
            return Page(page);
        }

        // Skipped: no rows and no counts, never 0.
        return IssueCatalog.Find(checkId) is null
            ? throw Problems.CheckNotFound(checkId)
            : new UrlPageDto(["id"], new UrlRowsDto(["id"], []), query.Offset, null, null, null, rows.Summary, []);
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
    public async Task<string> ReportAsync(string id, ReportOptions report, CancellationToken cancellationToken)
    {
        var path = crawls.PathOf(id);
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
