using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;
using SQLitePCL;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The only code that writes to a crawl database. One loop drains a bounded channel of commands and
/// applies them in batched transactions with prepared statements. Workers never wait on SQLite, and
/// the database never sees two writers. The writer also owns the frontier: claims of queued URLs go
/// through the same loop, which makes "nothing queued and nothing in flight" an exact completion test.
/// Memory stays flat because the only per-URL state held is a 128-bit key and a 12-byte entry.
/// </summary>
internal sealed class CrawlWriter : IAsyncDisposable
{
    private const int CHANNEL_CAPACITY = 1_024;
    private const int MAX_COMMANDS_PER_TRANSACTION = 2_000;
    private static readonly TimeSpan MAX_TRANSACTION_AGE = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the writer leaves a log it couldn't empty (a read outlasted its wait, or another program is part-way
    /// through the file) before it tries again: a second at first, twice as long each time it still can't, to half a
    /// minute at most.
    /// </summary>
    private static readonly TimeSpan LOG_RETRY = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LOG_LONGEST_RETRY = TimeSpan.FromSeconds(32);

    private readonly Channel<WriterCommand> _commands = Channel.CreateBounded<WriterCommand>(
        new BoundedChannelOptions(CHANNEL_CAPACITY) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly SqliteConnection _connection;
    private readonly CrawlFileLock _fileLock;
    private readonly WriterStatements _statements;
    private readonly CrawlOptions _options;
    private readonly CrawlCounters _counters;
    private readonly ILogger _logger;
    private readonly Dictionary<UInt128, UrlEntry> _urls = new();

    // Recorded pages whose depth dropped after their links were stored, to pass the shorter path on to.
    private readonly Queue<UInt128> _depthChanges = new();

    // Internal URLs queued again for a full fetch: counted against the URL limit when first claimed, not again.
    private readonly HashSet<int> _refetches = [];

    // Raised on any thread, stored by the loop: they wait here, not in the channel, so a full channel can't lose one.
    private readonly ConcurrentQueue<CrawlNotice> _notices = new();

    // The run's stretches of crawling as its clock last had them, waiting to be stored, as notices do; and where each is kept.
    private readonly ConcurrentQueue<RunStretch> _runTime = new();
    private readonly Dictionary<int, long> _stretchRows = [];
    private bool _keepsRunTime;

    // The file holds the site's icon, or has no table of this engine's to keep one in: either way none is wanted.
    private volatile bool _hasFavicon;
    private long _run = 1;
    private bool _crawlStamped;

    // The size the write-ahead log is folded at, when folding it was last tried, and how long it's left before the
    // next try: no time once it's been emptied, longer after each try that couldn't.
    private readonly long _logLimit = CrawlDatabase.LogLimitBytes;
    private long _logTriedAt;
    private TimeSpan _logLeftFor;
    private TimeSpan _logRetry = LOG_RETRY;

    // What folding the log cost this run, said once as it closes: how often, how often in vain, and the waits.
    private int _logFolds;
    private int _logFoldsInVain;
    private TimeSpan _logLongestFold;
    private TimeSpan _logFoldTime;
    private SqliteTransaction? _transaction;
    private Task _loop = Task.CompletedTask;
    private int _nextUrlId = 1;
    private long _lastNoticeId;
    private int _inFlight;

    // Internal URLs in flight that were claimed with a link depth, as opposed to sitemap-only ones.
    private int _linkedInFlight;
    private int _claimedInternal;
    private bool _urlLimitReached;

    private CrawlWriter(SqliteConnection connection, CrawlFileLock fileLock, CrawlOptions options, CrawlCounters counters, ILogger logger)
    {
        _connection = connection;
        _fileLock = fileLock;
        _options = options;
        _counters = counters;
        _logger = logger;
        _statements = new WriterStatements(connection);
    }

    /// <summary>Completes when the write loop ends; faults if the database failed.</summary>
    public Task Completion => _loop;

    public bool SitemapsDone { get; private set; }

    /// <summary>
    /// The file holds the site's icon, or can't keep one (its table is another program's): either way there's none to
    /// fetch. Read from any thread.
    /// </summary>
    public bool HasFavicon => _hasFavicon;

    /// <summary>
    /// The time the crawl's earlier runs spent crawling; none for a new crawl. Null when it isn't all known: a run before
    /// this one wasn't timed (an earlier engine's), or the file keeps its time in a table this engine can't use.
    /// </summary>
    public TimeSpan? TimeCrawledBefore { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Locks the crawl file, opens or creates the crawl database, restores a resumed crawl and marks it running, then
    /// starts the write loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Another crawl or analysis is writing to the file, it already holds a crawl and this isn't a resume, or it isn't a
    /// crawl this engine can read.
    /// </exception>
    public static CrawlWriter Open(string databasePath, CrawlOptions options, bool resume, CrawlCounters counters, ILogger logger)
    {
        var fileLock = CrawlFileLock.Acquire(databasePath);
        SqliteConnection? connection = null;
        try
        {
            connection = CrawlDatabase.OpenWriter(databasePath);
            var created = CrawlDatabase.EnsureSchema(connection);
            if (!created && !resume)
            {
                throw new InvalidOperationException($"'{databasePath}' already holds a crawl. Resume it or choose a new file.");
            }

            // A file from before notices were kept gets their table now, as it is continued.
            CrawlDatabase.Execute(connection, CrawlSchema.NOTICES);
            CheckNoticesTable(connection, databasePath);

            // And one from before the time spent crawling was kept, its table: what it spent before isn't known.
            CrawlDatabase.Execute(connection, CrawlSchema.RUN_TIME);

            // The columns for lanes 1 to 3 of the content fingerprint are added here, to a new file as to one an earlier engine
            // made, whose pages so far have none.
            AddMissingColumns(connection, "responses", CrawlSchema.RESPONSE_COLUMNS);

            // And the count of the sitemaps an index's file lists, its size, and each entry's lastmod.
            AddMissingColumns(connection, "sitemaps", CrawlSchema.SITEMAP_COLUMNS);
            AddMissingColumns(connection, "sitemap_urls", CrawlSchema.SITEMAP_URL_COLUMNS);

            // And the table for the site's icon: a continued crawl keeps the one it has.
            CrawlDatabase.Execute(connection, CrawlSchema.FAVICON);
            var writer = new CrawlWriter(connection, fileLock, options, counters, logger);
            writer.RestoreFavicon();
            if (created)
            {
                writer.InsertCrawlRow();
                writer._keepsRunTime = true;
            }
            else
            {
                // Before the run stamps the crawl row: the stamp tells whether every run so far was timed.
                var earlier = CrawlRunReader.Read(connection, transaction: null);
                writer.RestoreState(timed: earlier is not null);
                writer.RestoreRunTime(earlier);
            }

            writer._loop = Task.Run(writer.RunAsync);
            return writer;
        }
        catch
        {
            if (connection is not null)
            {
                CrawlDatabase.CloseWriter(connection, logger);
            }

            fileLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The notices table may be another program's: CREATE TABLE IF NOT EXISTS keeps whatever is there. Every notice is stored
    /// into the columns this engine made, so a table missing one, keyed by something other than the id, or with a column
    /// of its own that must have a value, would fail the crawl at its first notice. Refused before anything is written,
    /// in words that say why, as a file with views or triggers is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The table isn't one this engine can store notices in.</exception>
    private static void CheckNoticesTable(SqliteConnection connection, string databasePath)
    {
        Dictionary<string, (string Type, bool NotNull, bool HasDefault, bool IsKey)> columns = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, type, \"notnull\", dflt_value IS NOT NULL, pk FROM pragma_table_info('notices')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns[reader.GetString(0)] = (reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4) > 0);
            }
        }

        var hasEveryColumn = CrawlSchema.NOTICE_COLUMNS.All(columns.ContainsKey);
        var keyedById = hasEveryColumn && columns["id"].IsKey && columns["id"].Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) &&
                        columns.Values.Count(column => column.IsKey) == 1;
        var dataMayBeLeftOut = hasEveryColumn && !columns["data"].NotNull;
        var othersNeedNothing = columns
            .Where(column => !CrawlSchema.NOTICE_COLUMNS.Contains(column.Key, StringComparer.OrdinalIgnoreCase))
            .All(column => !column.Value.NotNull || column.Value.HasDefault);
        if (!(keyedById && dataMayBeLeftOut && othersNeedNothing))
        {
            throw new InvalidOperationException(
                $"'{databasePath}' can't be continued: another program changed the table it keeps its alerts in (notices), so new ones couldn't be stored there.");
        }
    }

    /// <summary>
    /// Adds the columns of a crawl-time table that a file made by an earlier engine lacks (<see cref="CrawlSchema.RESPONSE_COLUMNS"/>,
    /// <see cref="CrawlSchema.SITEMAP_COLUMNS"/>): each may be null, so the rows the file has keep none.
    /// </summary>
    private static void AddMissingColumns(SqliteConnection connection, string table, (string Name, string Type)[] columns)
    {
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var (name, type) in columns.Where(column => !existing.Contains(column.Name)))
        {
            CrawlDatabase.Execute(connection, $"ALTER TABLE {table} ADD COLUMN {name} {type}");
        }
    }

    public ValueTask RecordAsync(PageRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new RecordPageCommand(record), cancellationToken);

    public ValueTask DiscoverAsync(IReadOnlyList<OutLink> urls, DiscoverySource source, int? sitemapId, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new DiscoverCommand(urls, source, sitemapId), cancellationToken);

    /// <summary>A sitemap's URLs, each with its entry's lastmod (<see cref="CrawlSchema.SITEMAP_URL_COLUMNS"/>), in the same order.</summary>
    public ValueTask DiscoverAsync(IReadOnlyList<OutLink> urls, IReadOnlyList<long?> lastmods, int sitemapId, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new DiscoverCommand(urls, DiscoverySource.Sitemap, sitemapId) { Lastmods = lastmods }, cancellationToken);

    /// <param name="parkedLinked">Linked URLs in flight that won't be done for a long while, which sitemap-only URLs don't wait for.</param>
    public async Task<FrontierClaim> ClaimAsync(int internalCount, int externalCount, CancellationToken cancellationToken, int parkedLinked = 0)
    {
        var reply = new TaskCompletionSource<FrontierClaim>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new ClaimCommand(internalCount, externalCount, reply, parkedLinked), cancellationToken).ConfigureAwait(false);
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RecordRobotsAsync(RobotsRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new RecordRobotsCommand(record), cancellationToken);

    /// <summary>Keeps the site's icon with the crawl, unless it has one already: the first kept stands.</summary>
    public ValueTask RecordFaviconAsync(FaviconRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new RecordFaviconCommand(record), cancellationToken);

    public async Task<int> RegisterSitemapAsync(string url, int? parentId, CancellationToken cancellationToken)
    {
        var reply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new RegisterSitemapCommand(url, parentId, reply), cancellationToken).ConfigureAwait(false);
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask CompleteSitemapAsync(SitemapRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new CompleteSitemapCommand(record), cancellationToken);

    public ValueTask MarkSitemapsDoneAsync(CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new MarkSitemapsDoneCommand(), cancellationToken);

    public ValueTask UpdateStatusAsync(CrawlStatus status, bool isFinished, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new UpdateCrawlStatusCommand(status, isFinished), cancellationToken);

    /// <summary>
    /// Gives the notice the crawl's next id and stores it with the crawl, within a second or so, from any thread, without
    /// waiting: a busy writer stores it at its next commit, and closing stores whatever is left. Returns it with its id.
    /// </summary>
    public CrawlNotice RecordNotice(CrawlNotice notice)
    {
        var recorded = notice with { Id = Interlocked.Increment(ref _lastNoticeId) };
        _notices.Enqueue(recorded);
        _commands.Writer.TryWrite(StoreNoticesCommand.Instance);
        return recorded;
    }

    /// <summary>
    /// Keeps a stretch of the run's crawling as its clock has it now, from any thread, without waiting: stored with the
    /// writer's next batch, and whatever is left as it closes. The latest heard of a stretch stands, and one that has
    /// ended stays ended.
    /// </summary>
    public void RecordRunTime(RunStretch stretch)
    {
        _runTime.Enqueue(stretch);
        _commands.Writer.TryWrite(StoreNoticesCommand.Instance);
    }

    /// <summary>
    /// Stops accepting commands, drains and commits what is queued, then folds the WAL into the file. Never throws: a
    /// failed loop has already surfaced its error through <see cref="Completion"/>, and the file is closed either way.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _commands.Writer.TryComplete();
        try
        {
            await _loop.ConfigureAwait(false);
            StoreWhatIsLeft();
        }
        catch (Exception exception) when (_loop.IsFaulted)
        {
            // Already logged by the loop and surfaced to the session through Completion.
            _logger.LogDebug(exception, "Writer loop had faulted before shutdown");
            RecordFailure();
        }
        finally
        {
            if (_logFolds + _logFoldsInVain > 0)
            {
                _logger.LogInformation(
                    "Folded the write-ahead log {Folds} times while the crawl ran ({InVain} more tries couldn't): the crawl waited {TotalMs} ms for it in all, {LongestMs} ms at longest",
                    _logFolds, _logFoldsInVain, (long)_logFoldTime.TotalMilliseconds, (long)_logLongestFold.TotalMilliseconds);
            }

            _statements.Dispose();
            CrawlDatabase.CloseWriter(_connection, _logger);
            _fileLock.Dispose();
        }
    }

    private async Task RunAsync()
    {
        var reader = _commands.Reader;
        var transactionAge = new Stopwatch();
        var commandsInTransaction = 0;

        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var command))
                {
                    if (_transaction is null)
                    {
                        BeginTransaction();
                        transactionAge.Restart();
                    }

                    Apply(command);

                    if (++commandsInTransaction >= MAX_COMMANDS_PER_TRANSACTION || transactionAge.Elapsed >= MAX_TRANSACTION_AGE)
                    {
                        StoreNotices();
                        StoreRunTime();
                        CommitTransaction();
                        FoldLargeLog();
                        commandsInTransaction = 0;
                    }
                }

                if (_transaction is not null)
                {
                    StoreNotices();
                    StoreRunTime();
                    CommitTransaction();
                    FoldLargeLog();
                    commandsInTransaction = 0;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Crawl database writer failed; the crawl cannot continue");
            RollBackTransaction();
            _commands.Writer.TryComplete(exception);
            while (reader.TryRead(out var pending))
            {
                FailReply(pending, exception);
            }

            throw;
        }
    }

    private void Apply(WriterCommand command)
    {
        try
        {
            switch (command)
            {
                case RecordPageCommand record:
                    ApplyRecord(record.Record);
                    break;
                case DiscoverCommand discover:
                    ApplyDiscover(discover);
                    break;
                case ClaimCommand claim:
                    claim.Reply.TrySetResult(ApplyClaim(claim.InternalCount, claim.ExternalCount, claim.ParkedLinked));
                    break;
                case RecordRobotsCommand robots:
                    _statements.UpsertRobots(robots.Record);
                    break;
                case RecordFaviconCommand favicon:
                    ApplyFavicon(favicon.Record);
                    break;
                case RegisterSitemapCommand register:
                    register.Reply.TrySetResult(_statements.RegisterSitemap(register.Url, register.ParentId));
                    break;
                case CompleteSitemapCommand complete:
                    _statements.UpdateSitemap(complete.Record);
                    break;
                case MarkSitemapsDoneCommand:
                    _statements.MarkSitemapsDone();
                    SitemapsDone = true;
                    break;
                case UpdateCrawlStatusCommand status:
                    _statements.UpdateCrawlStatus(status.Status, status.IsFinished, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    _crawlStamped = true;
                    break;
                case StoreNoticesCommand:
                    // Notices, and the run's time: stored with the batch, before it commits.
                    break;
                default:
                    throw new InvalidOperationException($"Unknown writer command {command.GetType().Name}.");
            }
        }
        catch (Exception exception)
        {
            FailReply(command, exception);
            throw;
        }
    }

    private void ApplyRecord(PageRecord record)
    {
        var item = record.Item;
        var key = UrlKey.Compute(item.Url);
        var response = record.Response;
        _inFlight--;
        if (item.IsInternal && item.Depth < CrawlDepth.UNLINKED)
        {
            _linkedInFlight--;
        }

        // Promoted to a full crawl while its status check was in flight, and the check found something to crawl: fetch
        // it again in full rather than store the check. A site-version probe the site found meanwhile is fetched again
        // too unless it found a page: where a redirect leads must be found by the site rather than by a probe, and a
        // probe's single try that failed must get the retries the site's own URLs get.
        var entry = _urls[key];
        if ((item.Mode == FetchMode.Check && entry.Mode == FetchMode.Crawl && response.Outcome == FetchOutcome.Success) ||
            (item.Source == DiscoverySource.SiteVersion && !entry.IsProbe && response.Outcome != FetchOutcome.Success))
        {
            _urls[key] = entry with { State = UrlState.Queued };
            _statements.UpdateUrlState(item.Id, UrlState.Queued);
            _counters.OnReturned();
            NoteRefetch(item.Id, item.IsInternal);
            return;
        }

        // The page's depth as it is now: a shorter path may have turned up while it was being fetched.
        var depth = entry.Depth;
        int? redirectId = null;
        int? canonicalId = null;
        var outlinkCount = 0;

        foreach (var link in record.Links)
        {
            var targetId = ResolveLink(link, item.Id, depth, record.PageNoFollow, entry.IsProbe);
            _statements.InsertLink(item.Id, targetId, link);

            switch (link.Kind)
            {
                case LinkKind.Redirect:
                    redirectId ??= targetId;
                    break;
                case LinkKind.Canonical when (link.Flags & LinkFlags.OutsideHead) == 0:
                    // The canonical indexability goes by: from the head or an HTTP header, never one in the body.
                    canonicalId ??= targetId;
                    break;
                case LinkKind.Hyperlink:
                    outlinkCount++;
                    break;
            }
        }

        _statements.InsertResponse(response, redirectId, canonicalId, outlinkCount);
        if (record.Details is not null)
        {
            _statements.UpsertDetails(item.Id, record.Details);
        }

        if (record.Html is not null)
        {
            _statements.UpsertHtml(item.Id, record.Html);
        }

        _statements.UpdateUrlState(item.Id, UrlState.Done);
        _urls[key] = _urls[key] with { State = UrlState.Done };

        // A probe the site has since found was adopted meanwhile, so the response is one of the site's.
        _counters.OnRecorded(new RecordedResponse(response.Outcome, response.StatusCode, IsPage(item.IsInternal, item.Url, response),
            IsSiteUrl: item.IsInternal && !entry.IsProbe, IsNotChecked(response), DownloadedBytes(response.SizeBytes, response.BodyBytes), entry.IsProbe));
        PropagateDepths();
    }

    /// <summary>
    /// A page, as <see cref="CrawlSchema.PAGE"/> defines one for every stored count, so the live count ends where the
    /// stored one is: internal, HTML, fetched and parsed, and not one of Cloudflare's endpoints. A URL only status-checked
    /// has no word count.
    /// </summary>
    private static bool IsPage(bool isInternal, string url, ResponseRow response) =>
        isInternal && response is { IsHtml: true, Outcome: FetchOutcome.Success, WordCount: not null } && !CrawlSchema.IsCloudflareEndpoint(url);

    /// <summary>Failed without a request, its host having stopped answering, as <see cref="AnalysisReader.NOT_CHECKED"/> reads it back.</summary>
    private static bool IsNotChecked(ResponseRow response) =>
        response.Outcome == FetchOutcome.NetworkError && response.Error?.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) == true;

    /// <summary>
    /// The bytes of a body the crawler read. A size it was only told, by the Content-Length of a URL it checked
    /// without downloading, isn't. Matches the restore on resume, which goes by the stored body size the same way.
    /// </summary>
    private static long DownloadedBytes(long? sizeBytes, long? bodyBytes) => bodyBytes is null ? 0 : sizeBytes ?? 0;

    private void ApplyDiscover(DiscoverCommand command)
    {
        if (command.Source == DiscoverySource.SiteVersion)
        {
            // Claimed right behind the start URL, which shares their depth.
            foreach (var link in command.Urls)
            {
                Probe(link, 0, null);
            }

            return;
        }

        var depth = command.Source == DiscoverySource.Seed ? 0 : CrawlDepth.UNLINKED;

        for (var index = 0; index < command.Urls.Count; index++)
        {
            var link = command.Urls[index];

            // Sitemap and start URLs are crawled in full, whatever link found them first, and never held to the depth limit.
            var skip = !link.IsInternal && !_options.CheckExternalLinks ? SkipReason.ExternalsDisabled : SkipReason.None;
            int id;
            if (_urls.TryGetValue(link.Key, out var entry))
            {
                id = entry.Id;
                Rediscover(link, entry, depth, skip, exemptFromDepthLimit: true, command.Source, null);
            }
            else
            {
                id = Insert(link, depth, skip, command.Source, null, exemptFromDepthLimit: true);
            }

            if (command.SitemapId is { } sitemapId)
            {
                _statements.InsertSitemapUrl(sitemapId, id, command.Lastmods is { } lastmods && index < lastmods.Count ? lastmods[index] : null);
            }
        }

        PropagateDepths();
    }

    private FrontierClaim ApplyClaim(int internalCount, int externalCount, int parkedLinked)
    {
        var internalItems = internalCount > 0 ? ClaimInternal(internalCount, parkedLinked) : [];
        List<FrontierItem> externalItems = externalCount > 0 ? _statements.ClaimQueued(false, externalCount, 0, int.MaxValue) : [];

        foreach (var item in internalItems.Concat(externalItems))
        {
            SetState(item.Url, UrlState.Fetching);
        }

        var claimed = internalItems.Count + externalItems.Count;
        _inFlight += claimed;
        _counters.OnClaimed(claimed);
        return new FrontierClaim(internalItems, externalItems, _inFlight);
    }

    /// <summary>
    /// Claims the site's own URLs breadth-first. URLs only a sitemap lists wait until no linked URL is queued or in
    /// flight, so every link depth is settled first and they never take the place of a linked page. The URL limit is
    /// spent here, in that order, rather than as URLs are discovered. Linked URLs parked for a long while (a section the
    /// site limits) don't hold them up: one they lead to that a sitemap lists is then already read, as only listed.
    /// </summary>
    private List<FrontierItem> ClaimInternal(int count, int parkedLinked)
    {
        var limit = _options.MaxUrls > 0 && !_urlLimitReached ? Math.Min(count, _options.MaxUrls - _claimedInternal) : count;
        var items = _statements.ClaimQueued(true, limit, 0, CrawlDepth.UNLINKED);
        if (items.Count == 0 && _linkedInFlight <= parkedLinked)
        {
            items = _statements.ClaimQueued(true, limit, CrawlDepth.UNLINKED, int.MaxValue);
        }

        foreach (var item in items)
        {
            if (item.Depth < CrawlDepth.UNLINKED)
            {
                _linkedInFlight++;
            }

            // A refetch took its slot the first time, and a site-version probe takes none.
            var isRefetch = _refetches.Count > 0 && _refetches.Remove(item.Id);
            if (!isRefetch && item.Source != DiscoverySource.SiteVersion)
            {
                _claimedInternal++;
            }
        }

        SkipQueuedIfAtUrlLimit();
        return items;
    }

    /// <summary>Once the URL limit is reached, the internal URLs still queued are skipped, once.</summary>
    private void SkipQueuedIfAtUrlLimit()
    {
        if (_options.MaxUrls > 0 && !_urlLimitReached && _claimedInternal >= _options.MaxUrls)
        {
            _urlLimitReached = true;
            SkipQueuedInternal();
        }
    }

    /// <summary>The URL limit is reached: internal URLs still queued are skipped, except those already counted.</summary>
    private void SkipQueuedInternal()
    {
        var skipped = 0;
        foreach (var (id, url) in _statements.SkipQueuedInternal())
        {
            var key = UrlKey.Compute(url);
            var entry = _urls[key];
            if (_refetches.Contains(id))
            {
                // A status check waiting to be fetched again in full; its slot was taken the first time.
                _statements.RequeueUrl(id, entry.Mode, entry.Depth);
                continue;
            }

            _urls[key] = entry with { State = UrlState.Skipped, Skip = SkipReason.UrlLimit };
            skipped++;
        }

        _counters.OnUrlsSkipped(skipped);
    }

    /// <param name="sourceIsProbe">The link is on a site-version probe (a redirect): see <see cref="Probe"/>.</param>
    private int ResolveLink(OutLink link, int sourceId, int sourceDepth, bool pageNoFollow, bool sourceIsProbe)
    {
        var depth = link.Kind.KeepsDepth() ? sourceDepth : CrawlDepth.Next(sourceDepth);
        if (sourceIsProbe)
        {
            return Probe(link, depth, sourceId);
        }

        var skip = FollowDecision(link, pageNoFollow);

        // Depth-limited crawls don't follow links on pages only a sitemap lists, or the sitemap would lift the limit
        // for everything those pages lead to. A redirect is part of fetching the listed URL, so it keeps the exemption.
        var exemptFromDepthLimit = sourceDepth >= CrawlDepth.UNLINKED && link.Kind.KeepsDepth();
        if (_urls.TryGetValue(link.Key, out var entry))
        {
            Rediscover(link, entry, depth, skip, exemptFromDepthLimit, DiscoverySource.Link, sourceId);
            return entry.Id;
        }

        return Insert(link, depth, skip, DiscoverySource.Link, sourceId, exemptFromDepthLimit);
    }

    /// <summary>
    /// A URL a site-version probe leads to: another version of the site at the start of the crawl, or where a probe
    /// redirects. A new one gets a status check and nothing more, never parsed and never followed, so a version that
    /// answers with the site can't get the site crawled twice. It isn't held to the URL limit. A URL already known is
    /// left as it is: a probe changes nothing the site itself led to.
    /// </summary>
    private int Probe(OutLink link, int depth, int? foundOnId)
    {
        if (_urls.TryGetValue(link.Key, out var entry))
        {
            return entry.Id;
        }

        var skip = !link.IsInternal && !_options.CheckExternalLinks ? SkipReason.ExternalsDisabled : SkipReason.None;
        return Insert(link with { IsCrawlable = false }, depth, skip, DiscoverySource.SiteVersion, foundOnId, exemptFromDepthLimit: true);
    }

    /// <summary>
    /// A known URL found again, so that what is stored for it doesn't depend on the order links and sitemaps turn up
    /// in. Any link can shorten its depth. A followed link or a sitemap listing can queue it or promote it to a full
    /// crawl; a link that isn't followed only leaves the strongest reason for skipping it.
    /// </summary>
    /// <param name="skip">Why this discovery alone would not be fetched, or None.</param>
    /// <param name="source">How it was found this time, which a URL only a site-version probe had found takes on.</param>
    private void Rediscover(OutLink link, UrlEntry entry, int depth, SkipReason skip, bool exemptFromDepthLimit,
        DiscoverySource source, int? foundOnId)
    {
        if (entry.IsProbe)
        {
            // Adopting takes one of the URL limit's slots. Once none is left, the site finding a probe is like it finding
            // any other URL then, which is skipped: the URL stays a probe, keeps the answer its probe got, and is never
            // fetched again, so the site's own URLs found and fetched never pass the limit.
            if (link.IsInternal && _urlLimitReached)
            {
                return;
            }

            entry = Adopt(link, entry, depth, source, foundOnId);
        }
        else if (depth < entry.Depth)
        {
            entry = LowerDepth(link.Key, entry, depth, link.IsInternal);
        }

        if (skip == SkipReason.None)
        {
            Revisit(link, entry, exemptFromDepthLimit);
        }
        else if (entry.State == UrlState.Skipped && skip > entry.Skip)
        {
            _urls[link.Key] = SetSkip(entry, skip, ModeFor(link));
        }
    }

    /// <summary>
    /// A known URL reached through a followed link or listed in a sitemap. If it was skipped only for a reason this
    /// overrides, it is queued. A status check becomes a full crawl when the URL can be crawled: queued or in flight,
    /// it switches mode; already done, it is fetched again in full if the check found something to crawl.
    /// </summary>
    private void Revisit(OutLink link, UrlEntry entry, bool exemptFromDepthLimit)
    {
        var mode = ModeFor(link);
        var updated = entry;
        if (entry.State == UrlState.Skipped)
        {
            if (entry.Skip is SkipReason.NoFollow or SkipReason.NotFollowedByOption or SkipReason.ResourcesDisabled or SkipReason.DepthLimit)
            {
                var skip = Accept(link.IsInternal, mode, entry.Depth, exemptFromDepthLimit);
                updated = skip == SkipReason.None ? Requeue(entry, mode, isSiteUrl: link.IsInternal)
                    : skip > entry.Skip ? SetSkip(entry, skip, mode)
                    : entry;
            }
        }
        else if (entry.Mode == FetchMode.Check && mode == FetchMode.Crawl && WithinDepthLimit(entry.Depth, exemptFromDepthLimit))
        {
            // A URL in flight keeps its claim; the record of its check comes back here and is queued again.
            updated = entry.State == UrlState.Done ? PromoteChecked(entry, link.IsInternal) : SetMode(entry, FetchMode.Crawl);
        }

        if (updated != entry)
        {
            _urls[link.Key] = updated;
        }
    }

    /// <summary>
    /// Any link counts for depth, followed or not, so a URL's depth is its shortest path from the start whichever order
    /// the links arrive in. A URL that was too deep to crawl and now isn't is queued, and a page already recorded passes
    /// the shorter path on to its links.
    /// </summary>
    private UrlEntry LowerDepth(UInt128 key, UrlEntry entry, int depth, bool isInternal)
    {
        _statements.UpdateUrlDepth(entry.Id, depth);
        var updated = entry with { Depth = depth };
        if (updated is { State: UrlState.Skipped, Skip: SkipReason.DepthLimit })
        {
            // Only a followed page link is ever too deep, and it still leads here, now by a shorter path.
            var skip = Accept(isInternal, FetchMode.Crawl, depth, exemptFromDepthLimit: false);
            updated = skip == SkipReason.None ? Requeue(updated, FetchMode.Crawl, isSiteUrl: isInternal && !updated.IsProbe)
                : skip > updated.Skip ? SetSkip(updated, skip, FetchMode.Crawl)
                : updated;
        }
        else if (updated.State == UrlState.Done)
        {
            _depthChanges.Enqueue(key);
        }

        _urls[key] = updated;
        return updated;
    }

    /// <summary>
    /// Passes depth reductions on through the links of pages already recorded, so depth is the shortest path even when
    /// the page with the shorter path loaded last. Rare: breadth-first, links mostly arrive in depth order.
    /// </summary>
    private void PropagateDepths()
    {
        while (_depthChanges.TryDequeue(out var key))
        {
            var source = _urls[key];
            foreach (var (kind, url, isInternal) in _statements.ReadLinkTargets(source.Id))
            {
                var depth = kind.KeepsDepth() ? source.Depth : CrawlDepth.Next(source.Depth);
                var targetKey = UrlKey.Compute(url);
                if (_urls.TryGetValue(targetKey, out var target) && depth < target.Depth)
                {
                    LowerDepth(targetKey, target, depth, isInternal);
                }
            }
        }
    }

    /// <summary>
    /// A URL already status-checked that can now be crawled. A full fetch only differs when the check succeeded (a
    /// page's body, the links in its headers), so only then is it queued again; otherwise the check stands for it.
    /// </summary>
    private UrlEntry PromoteChecked(UrlEntry entry, bool isInternal) =>
        _statements.ReadOutcome(entry.Id) is { Outcome: FetchOutcome.Success } check
            ? Refetch(entry, FetchMode.Crawl, isInternal, check, wasSiteUrl: isInternal && !entry.IsProbe, wasProbe: entry.IsProbe)
            : SetMode(entry, FetchMode.Crawl);

    /// <summary>Drops what a recorded URL's fetch stored and queues it again, to be fetched in <paramref name="mode"/>.</summary>
    /// <param name="wasSiteUrl">The response was counted as one of the site's own URLs, rather than a probe's.</param>
    /// <param name="wasProbe">The response was counted as a probe's, on the site or off it.</param>
    private UrlEntry Refetch(UrlEntry entry, FetchMode mode, bool isInternal, StoredOutcome stored, bool wasSiteUrl, bool wasProbe)
    {
        _statements.DeleteResponse(entry.Id);
        _statements.RequeueUrl(entry.Id, mode, entry.Depth);
        _counters.OnResponseRemoved(new RecordedResponse(stored.Outcome, stored.StatusCode, isInternal && stored.IsPage, wasSiteUrl,
            stored.IsNotChecked, stored.Bytes, wasProbe));
        NoteRefetch(entry.Id, isInternal);
        return entry with { State = UrlState.Queued, Mode = mode };
    }

    /// <summary>
    /// A URL only a site-version probe had found, found now by the site itself: linked, listed in a sitemap, or where
    /// the start URL redirects. From here on it is stored as found that way, at that depth (a probe's depth is no
    /// click path), and the analysis judges it as one of the site's own URLs. If the probe already fetched it, it takes
    /// its slot under the URL limit now. If that fetch found anything but a page it is fetched again: where a redirect
    /// leads is then found by the site too and crawled as the site's own, and a probe's single try that failed gets the
    /// retries the site's own URLs get. Never once the URL limit is reached (see <see cref="Rediscover"/>).
    /// </summary>
    private UrlEntry Adopt(OutLink link, UrlEntry entry, int depth, DiscoverySource source, int? foundOnId)
    {
        _statements.UpdateUrlSource(entry.Id, source, foundOnId, link.Kind, depth);
        var adopted = entry with { Depth = depth, IsProbe = false };
        _urls[link.Key] = adopted;

        // A probe's answer that is kept counts as the site's from now on; one fetched again is counted when it's back.
        var stored = entry.State == UrlState.Done ? _statements.ReadOutcome(entry.Id) : null;
        var refetch = stored is { Outcome: not FetchOutcome.Success };
        if (link.IsInternal)
        {
            _counters.OnAdopted(found: entry.State != UrlState.Skipped, fetched: stored is not null && !refetch);
        }

        if (stored is { } kept && !refetch)
        {
            _counters.OnProbeResponseAdopted(kept.Outcome, kept.StatusCode, kept.IsNotChecked);
        }

        if (link.IsInternal && entry.State is UrlState.Fetching or UrlState.Done)
        {
            _claimedInternal++;
            SkipQueuedIfAtUrlLimit();
        }

        if (refetch)
        {
            adopted = Refetch(adopted, adopted.Mode, link.IsInternal, stored!.Value, wasSiteUrl: false, wasProbe: true);
            _urls[link.Key] = adopted;
        }

        return adopted;
    }

    private void NoteRefetch(int id, bool isInternal)
    {
        if (isInternal && _options.MaxUrls > 0)
        {
            _refetches.Add(id);
        }
    }

    /// <param name="isSiteUrl">One of the site's own URLs: internal, and not a probe of its other versions.</param>
    private UrlEntry Requeue(UrlEntry entry, FetchMode mode, bool isSiteUrl)
    {
        _statements.RequeueUrl(entry.Id, mode, entry.Depth);
        _counters.OnUrlRequeued(isSiteUrl);
        return entry with { State = UrlState.Queued, Skip = SkipReason.None, Mode = mode };
    }

    private UrlEntry SetSkip(UrlEntry entry, SkipReason skip, FetchMode mode)
    {
        _statements.UpdateUrlSkip(entry.Id, skip, mode);
        return entry with { Skip = skip, Mode = mode };
    }

    private UrlEntry SetMode(UrlEntry entry, FetchMode mode)
    {
        _statements.UpdateUrlMode(entry.Id, mode);
        return entry with { Mode = mode };
    }

    private static FetchMode ModeFor(OutLink link) => link.IsCrawlable ? FetchMode.Crawl : FetchMode.Check;

    private int Insert(OutLink link, int depth, SkipReason skip, DiscoverySource source, int? foundOnId, bool exemptFromDepthLimit)
    {
        var mode = ModeFor(link);
        var isProbe = source == DiscoverySource.SiteVersion;

        // A probe is only status-checked, which no depth limit holds back, and the URL limit doesn't count it.
        if (skip == SkipReason.None && !isProbe)
        {
            skip = Accept(link.IsInternal, mode, depth, exemptFromDepthLimit);
        }

        var state = skip == SkipReason.None ? UrlState.Queued : UrlState.Skipped;
        var id = _nextUrlId++;
        _statements.InsertUrl(id, link, mode, depth, state, skip, source, foundOnId);
        _urls[link.Key] = new UrlEntry(id, depth, state, skip, mode, isProbe);
        _counters.OnUrlAdded(state == UrlState.Queued, isSiteUrl: link.IsInternal && !isProbe);
        return id;
    }

    /// <summary>
    /// Applies the depth and URL limits to a URL about to be queued. Status checks have no depth limit, and the URL
    /// limit is spent as URLs are claimed: once it is reached, no more internal URLs are queued.
    /// </summary>
    private SkipReason Accept(bool isInternal, FetchMode mode, int depth, bool exemptFromDepthLimit)
    {
        if (mode == FetchMode.Crawl && !WithinDepthLimit(depth, exemptFromDepthLimit))
        {
            return SkipReason.DepthLimit;
        }

        return isInternal && _urlLimitReached ? SkipReason.UrlLimit : SkipReason.None;
    }

    private bool WithinDepthLimit(int depth, bool exemptFromDepthLimit) =>
        exemptFromDepthLimit || _options.MaxDepth == 0 || depth <= _options.MaxDepth;

    private SkipReason FollowDecision(OutLink link, bool pageNoFollow)
    {
        var noFollow = pageNoFollow || (link.Flags & LinkFlags.NoFollow) != 0;
        var reason = link.Kind switch
        {
            LinkKind.Hyperlink when noFollow && !_options.FollowNofollow => SkipReason.NoFollow,
            LinkKind.Canonical when !_options.CrawlCanonicals => SkipReason.NotFollowedByOption,
            LinkKind.Hreflang when !_options.CrawlHreflang => SkipReason.NotFollowedByOption,
            LinkKind.Pagination when !_options.CrawlPagination => SkipReason.NotFollowedByOption,
            LinkKind.Image or LinkKind.Script or LinkKind.Stylesheet or LinkKind.Media or LinkKind.Icon or LinkKind.Iframe
                when !_options.CheckResources => SkipReason.ResourcesDisabled,
            _ => SkipReason.None
        };

        return reason == SkipReason.None && !link.IsInternal && !_options.CheckExternalLinks
            ? SkipReason.ExternalsDisabled
            : reason;
    }

    private void SetState(string url, UrlState state)
    {
        var key = UrlKey.Compute(url);
        if (_urls.TryGetValue(key, out var entry))
        {
            _urls[key] = entry with { State = state };
        }
    }

    private void InsertCrawlRow()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO crawl (id, start_url, options_json, status, started_at, updated_at, engine_version)
            VALUES (1, $url, $options, $status, $now, $now, $version)
            """;
        command.Parameters.AddWithValue("$url", _options.StartUrl);
        command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(_options.WithoutSecrets(), EngineJsonContext.Default.CrawlOptions));
        command.Parameters.AddWithValue("$status", (int)CrawlStatus.Running);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$version", EngineInfo.Version);
        command.ExecuteNonQuery();
    }

    /// <param name="timed">The file knows the time every run so far spent crawling, and must go on knowing it while this one starts.</param>
    private void RestoreState(bool timed)
    {
        // In one transaction: URLs claimed but never recorded when the last run stopped go back on the queue, and the
        // crawl is marked running again. A status of Running left by a crash is fine; the lock says nothing else writes.
        using (var transaction = _connection.BeginTransaction())
        {
            _statements.SetTransaction(transaction);
            CrawlDatabase.Execute(_connection, "UPDATE urls SET state = 0 WHERE state = 1;", transaction);
            _statements.UpdateCrawlStatus(CrawlStatus.Running, false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (timed)
            {
                // The stamp just moved is this engine's doing: the last stretch takes it with the same commit, so a
                // reader never finds the crawl stamped past what was timed while a big crawl's URLs are read back in.
                PassStampOn(transaction);
            }

            transaction.Commit();
        }

        _statements.SetTransaction(null);
        SitemapsDone = Convert.ToInt64(CrawlDatabase.Scalar(_connection, "SELECT sitemaps_done FROM crawl WHERE id = 1")) == 1;
        _lastNoticeId = Convert.ToInt64(CrawlDatabase.Scalar(_connection, "SELECT COALESCE(MAX(id), 0) FROM notices"), CultureInfo.InvariantCulture);

        long queued = 0;
        long skipped = 0;
        long siteUrlsFound = 0;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT id, url, depth, state, skip_reason, fetch_mode, is_internal, source FROM urls";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new UrlEntry(reader.GetInt32(0), reader.GetInt32(2), (UrlState)reader.GetInt32(3),
                    (SkipReason)reader.GetInt32(4), (FetchMode)reader.GetInt32(5), reader.GetInt32(7) == (int)DiscoverySource.SiteVersion);
                _urls[UrlKey.Compute(reader.GetString(1))] = entry;
                _nextUrlId = Math.Max(_nextUrlId, entry.Id + 1);

                if (entry.State == UrlState.Skipped)
                {
                    skipped++;
                    continue;
                }

                var isSiteUrl = reader.GetBoolean(6) && !entry.IsProbe;
                siteUrlsFound += isSiteUrl ? 1 : 0;
                if (entry.State == UrlState.Queued)
                {
                    queued++;
                }
                else if (isSiteUrl)
                {
                    // Fetched already, so it took one of the URL limit's slots. A site-version probe takes none.
                    _claimedInternal++;
                }
            }
        }

        _counters.RestoreUrls(_urls.Count, queued, skipped, siteUrlsFound);
        SkipQueuedIfAtUrlLimit();

        // Grouped as the live counts are kept: by outcome and status code, the probes of the site's other versions apart.
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT r.outcome, r.status_code, COUNT(*),
                       SUM(CASE WHEN {CrawlSchema.PAGE} THEN 1 ELSE 0 END),
                       COALESCE(SUM(CASE WHEN r.body_bytes IS NOT NULL THEN r.size_bytes END), 0),
                       SUM(u.is_internal = 1 AND u.source <> {(int)DiscoverySource.SiteVersion}),
                       SUM(r.outcome = {(int)FetchOutcome.NetworkError} AND {AnalysisReader.NOT_CHECKED}),
                       u.source = {(int)DiscoverySource.SiteVersion}
                FROM responses r JOIN urls u ON u.id = r.url_id
                GROUP BY r.outcome, r.status_code, u.source = {(int)DiscoverySource.SiteVersion}
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                _counters.AddResponses((FetchOutcome)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetInt64(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetBoolean(7));
            }
        }

        _counters.Publish();
        _logger.LogInformation("Resuming crawl with {Known} known URLs, {Queued} still queued", _urls.Count, queued);
    }

    /// <summary>Stores the notices raised since the last batch, in the batch about to commit.</summary>
    private void StoreNotices()
    {
        while (_notices.TryDequeue(out var notice))
        {
            _statements.InsertNotice(notice);
        }
    }

    /// <summary>
    /// The loop has ended, so notices raised as it closed, and the run's last stretch of crawling if it ended since, are
    /// stored on their own. Never throws: whatever stops this (a full disk) has its own error already, and the crawl's
    /// state is stored either way.
    /// </summary>
    private void StoreWhatIsLeft()
    {
        if (_notices.IsEmpty && _runTime.IsEmpty && !_crawlStamped)
        {
            return;
        }

        try
        {
            BeginTransaction();
            StoreNotices();
            StoreRunTime();
            CommitTransaction();
        }
        catch (SqliteException exception)
        {
            _logger.LogWarning(exception, "Could not store the crawl's last notices and the time it spent crawling");
            RollBackTransaction();
        }
    }

    /// <summary>
    /// Whether the favicon table is this engine's, and holds an icon. A table of another shape is another program's
    /// (CREATE TABLE IF NOT EXISTS keeps whatever is there): no icon is kept in it, and none is fetched.
    /// </summary>
    private void RestoreFavicon()
    {
        var isOurs = RunTime("SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info('favicon') ORDER BY cid)", transaction: null) as string
                     == CrawlSchema.FAVICON_COLUMNS;
        if (!isOurs)
        {
            _logger.LogWarning("Another program changed the table the site's icon is kept in (favicon), so none is fetched");
        }

        _hasFavicon = !isOurs || RunTime("SELECT 1 FROM favicon LIMIT 1", transaction: null) is not null;
    }

    /// <summary>
    /// Stores the site's icon in the batch under way: the first one offered, and only into a table of this engine's shape.
    /// A table that has the engine's columns and still refuses the row (a constraint of another program's) costs the
    /// crawl its icon, never the crawl: the statement alone is undone, and the batch goes on.
    /// </summary>
    private void ApplyFavicon(FaviconRecord record)
    {
        if (_hasFavicon)
        {
            return;
        }

        _hasFavicon = true;
        try
        {
            RunTime("INSERT INTO favicon (id, url, content_type, width, height, fetched_at, body) VALUES (1, $url, $type, $width, $height, $fetchedAt, $body)", _transaction,
                ("$url", record.Url), ("$type", record.ContentType), ("$width", record.Width), ("$height", record.Height), ("$fetchedAt", record.FetchedAt),
                ("$body", record.Body));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is raw.SQLITE_CONSTRAINT or raw.SQLITE_MISMATCH)
        {
            _logger.LogWarning(exception, "The site's icon couldn't be kept in the crawl file");
        }
    }

    /// <summary>
    /// What the file knows of the time spent crawling before this run, as it was before the run stamped the crawl row, and
    /// which run this one is. Where that isn't the whole of it (the file is from before the time was kept, or an engine
    /// that doesn't keep it ran the crawl since), a row without a time says so for good: the crawl's time then isn't
    /// known, however many runs are timed from here on.
    /// </summary>
    private void RestoreRunTime(CrawlRuns? earlier)
    {
        using var transaction = _connection.BeginTransaction();
        _keepsRunTime = RunTime("SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info('run_time') ORDER BY cid)", transaction) as string
                        == CrawlSchema.RUN_TIME_COLUMNS;
        if (!_keepsRunTime)
        {
            TimeCrawledBefore = null;
            _logger.LogWarning("Another program changed the table the time spent crawling is kept in (run_time), so this run's isn't kept");
            return;
        }

        // A stretch left going by a run whose process died ended then, as near as the file knows.
        RunTime("UPDATE run_time SET ended = 1 WHERE ended = 0", transaction);
        var lastRun = RunTime("SELECT MAX(run) FROM run_time WHERE typeof(run) = 'integer'", transaction) as long? ?? 0;
        var untimed = RunTime("SELECT 1 FROM run_time WHERE crawled_ms IS NULL LIMIT 1", transaction) is not null;

        // No stretch kept and nothing fetched: no run has crawled yet, whichever engine made the file (one paused before
        // it began and then stopped, or whose process died before its first batch was stored), so none went untimed.
        // A stretch is stored with the first batch that stores a response, so a file with responses and no stretch was
        // crawled by an engine that keeps no time.
        var neverCrawled = lastRun == 0 && !untimed && RunTime("SELECT 1 FROM responses LIMIT 1", transaction) is null;
        if (earlier is null && !untimed && !neverCrawled)
        {
            // From the crawl's start when nothing was ever timed, else from where the last stretch known ended.
            RunTime("""
                INSERT INTO run_time (run, started_at, crawled_ms, ended, crawl_updated_at)
                SELECT $run, COALESCE((SELECT started_at + COALESCE(crawled_ms, 0) FROM run_time ORDER BY id DESC LIMIT 1), started_at), NULL, 1, updated_at
                FROM crawl WHERE id = 1
                """, transaction, ("$run", ++lastRun));
        }

        TimeCrawledBefore = earlier?.TimeCrawling ?? (neverCrawled ? TimeSpan.Zero : null);
        _run = lastRun + 1;
        transaction.Commit();
    }

    /// <summary>
    /// Stores the run's stretches of crawling as last heard, in the batch about to commit: each in its own row, the latest
    /// time heard of it, and ended once it has. A batch that stamped the crawl row (the run's end) passes the stamp on to
    /// the last stretch, which is how a reader knows no run went untimed since.
    /// </summary>
    private void StoreRunTime()
    {
        if (!_keepsRunTime)
        {
            _runTime.Clear();
            return;
        }

        while (_runTime.TryDequeue(out var stretch))
        {
            if (_stretchRows.TryGetValue(stretch.Index, out var row))
            {
                RunTime("UPDATE run_time SET crawled_ms = MAX(crawled_ms, $ms), ended = MAX(ended, $ended) WHERE id = $id", _transaction,
                    ("$ms", stretch.CrawledMs), ("$ended", stretch.Ended ? 1 : 0), ("$id", row));
            }
            else
            {
                _stretchRows[stretch.Index] = (long)RunTime("""
                    INSERT INTO run_time (run, started_at, crawled_ms, ended, crawl_updated_at)
                    SELECT $run, $startedAt, $ms, $ended, updated_at FROM crawl WHERE id = 1
                    RETURNING id
                    """, _transaction, ("$run", _run), ("$startedAt", stretch.StartedAt.ToUnixTimeMilliseconds()), ("$ms", stretch.CrawledMs),
                    ("$ended", stretch.Ended ? 1 : 0))!;
            }
        }

        if (_crawlStamped)
        {
            PassStampOn(_transaction);
            _crawlStamped = false;
        }
    }

    /// <summary>
    /// The last stretch stored takes the crawl row's stamp as it stands, whichever run it's of (a run paused before it
    /// began has none of its own): this writer moved the stamp, and what it crawls it times, so no run has gone untimed.
    /// </summary>
    private void PassStampOn(SqliteTransaction? transaction) =>
        RunTime("UPDATE run_time SET crawl_updated_at = (SELECT updated_at FROM crawl WHERE id = 1) WHERE id = (SELECT MAX(id) FROM run_time)", transaction);

    private object? RunTime(string sql, SqliteTransaction? transaction, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private void BeginTransaction()
    {
        _transaction = _connection.BeginTransaction();
        _statements.SetTransaction(_transaction);
    }

    /// <summary>Stores the batch, then publishes the counts as they stand: what progress reads is what the file has.</summary>
    private void CommitTransaction()
    {
        _transaction!.Commit();
        _transaction.Dispose();
        _transaction = null;
        _statements.SetTransaction(null);
        _counters.Publish();
    }

    /// <summary>
    /// Between two batches: folds the write-ahead log into the file and empties it once it has passed its limit, which
    /// SQLite can't do by itself while the grids' reads follow one another closely (<see cref="CrawlDatabase.FoldLog"/>).
    /// No read is stopped for it: new reads wait while those in flight end, and so does this loop, which stores nothing
    /// meanwhile, so the log is held to its limit and the batch that took it past. A fold that couldn't be done (a read
    /// that outlasted the wait, or another program part-way through the file) cost the crawl that wait for nothing, so
    /// the next is tried a second later, and twice as long after each one that fails, to half a minute at most.
    /// </summary>
    private void FoldLargeLog()
    {
        if (CrawlDatabase.LogBytes(_connection.DataSource) < _logLimit || (_logTriedAt != 0 && Stopwatch.GetElapsedTime(_logTriedAt) < _logLeftFor))
        {
            return;
        }

        var began = Stopwatch.GetTimestamp();
        var emptied = CrawlDatabase.FoldLog(_connection, _logger);
        _logTriedAt = Stopwatch.GetTimestamp();
        var took = Stopwatch.GetElapsedTime(began, _logTriedAt);
        (_logFoldTime, _logLongestFold) = (_logFoldTime + took, took > _logLongestFold ? took : _logLongestFold);
        if (emptied)
        {
            _logFolds++;
            (_logLeftFor, _logRetry) = (TimeSpan.Zero, LOG_RETRY);
            return;
        }

        _logFoldsInVain++;

        (_logLeftFor, _logRetry) = (_logRetry, TimeSpan.FromTicks(Math.Min(_logRetry.Ticks * 2, LOG_LONGEST_RETRY.Ticks)));
        _logger.LogDebug("Readers kept the write-ahead log from being emptied; it is tried again in {Seconds} s", _logLeftFor.TotalSeconds);
    }

    /// <summary>Abandons the batch a failure interrupted, so the connection is free to be closed.</summary>
    private void RollBackTransaction()
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            _transaction.Dispose();
        }
        catch (SqliteException exception)
        {
            // SQLite rolls some failures back by itself (a full disk, an I/O error), leaving nothing to undo.
            _logger.LogDebug(exception, "Rolling back the failed batch reported an error");
        }
        finally
        {
            _transaction = null;
            _statements.SetTransaction(null);
        }
    }

    /// <summary>
    /// Marks the crawl failed so the file doesn't claim it is still running. Best effort: whatever stopped the writer
    /// (a full disk, say) may stop this too, and then the file reads as an interrupted crawl, which resumes the same way.
    /// </summary>
    private void RecordFailure()
    {
        try
        {
            _statements.UpdateCrawlStatus(CrawlStatus.Failed, false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _crawlStamped = true;
            StoreRunTime();
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Could not record the failure in the crawl database");
        }
    }

    private static void FailReply(WriterCommand command, Exception exception)
    {
        switch (command)
        {
            case ClaimCommand claim:
                claim.Reply.TrySetException(exception);
                break;
            case RegisterSitemapCommand register:
                register.Reply.TrySetException(exception);
                break;
        }
    }
}
