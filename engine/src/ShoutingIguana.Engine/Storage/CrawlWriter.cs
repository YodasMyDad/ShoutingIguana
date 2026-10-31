using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

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

    private readonly Channel<WriterCommand> _commands = Channel.CreateBounded<WriterCommand>(
        new BoundedChannelOptions(CHANNEL_CAPACITY) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly SqliteConnection _connection;
    private readonly CrawlFileLock _fileLock;
    private readonly WriterStatements _statements;
    private readonly CrawlOptions _options;
    private readonly CrawlCounters _counters;
    private readonly ILogger _logger;
    private readonly Dictionary<UInt128, UrlEntry> _urls = new();
    private SqliteTransaction? _transaction;
    private Task _loop = Task.CompletedTask;
    private int _nextUrlId = 1;
    private int _inFlight;
    private int _acceptedInternal;

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

            var writer = new CrawlWriter(connection, fileLock, options, counters, logger);
            if (created)
            {
                writer.InsertCrawlRow();
            }
            else
            {
                writer.RestoreState();
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

    public ValueTask RecordAsync(PageRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new RecordPageCommand(record), cancellationToken);

    public ValueTask DiscoverAsync(IReadOnlyList<OutLink> urls, DiscoverySource source, int? sitemapId, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new DiscoverCommand(urls, source, sitemapId), cancellationToken);

    public async Task<FrontierClaim> ClaimAsync(int internalCount, int externalCount, CancellationToken cancellationToken)
    {
        var reply = new TaskCompletionSource<FrontierClaim>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new ClaimCommand(internalCount, externalCount, reply), cancellationToken).ConfigureAwait(false);
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RecordRobotsAsync(RobotsRecord record, CancellationToken cancellationToken) =>
        _commands.Writer.WriteAsync(new RecordRobotsCommand(record), cancellationToken);

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
    /// Stops accepting commands, drains and commits what is queued, then folds the WAL into the file. Never throws: a
    /// failed loop has already surfaced its error through <see cref="Completion"/>, and the file is closed either way.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _commands.Writer.TryComplete();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception exception) when (_loop.IsFaulted)
        {
            // Already logged by the loop and surfaced to the session through Completion.
            _logger.LogDebug(exception, "Writer loop had faulted before shutdown");
            RecordFailure();
        }
        finally
        {
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
                        CommitTransaction();
                        commandsInTransaction = 0;
                    }
                }

                if (_transaction is not null)
                {
                    CommitTransaction();
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
                    claim.Reply.TrySetResult(ApplyClaim(claim.InternalCount, claim.ExternalCount));
                    break;
                case RecordRobotsCommand robots:
                    _statements.UpsertRobots(robots.Record);
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

        // Promoted to a full crawl while its status check was in flight, and the check found something to crawl:
        // fetch it again in full rather than store the check.
        if (item.Mode == FetchMode.Check && _urls[key].Mode == FetchMode.Crawl && response.Outcome == FetchOutcome.Success)
        {
            _urls[key] = _urls[key] with { State = UrlState.Queued };
            _statements.UpdateUrlState(item.Id, UrlState.Queued);
            _counters.OnReturned();
            return;
        }

        int? redirectId = null;
        int? canonicalId = null;
        var outlinkCount = 0;

        foreach (var link in record.Links)
        {
            var targetId = ResolveLink(link, item, record.PageNoFollow);
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

        var isSitePage = item.IsInternal && response.IsHtml && response.Outcome == FetchOutcome.Success;
        _counters.OnRecorded(response.Outcome, isSitePage, response.SizeBytes ?? 0);
    }

    private void ApplyDiscover(DiscoverCommand command)
    {
        var depth = command.Source == DiscoverySource.Seed ? 0 : CrawlDepth.UNLINKED;

        foreach (var link in command.Urls)
        {
            // Sitemap and start URLs are crawled in full, whatever link found them first, and never held to the depth limit.
            var skip = !link.IsInternal && !_options.CheckExternalLinks ? SkipReason.ExternalsDisabled : SkipReason.None;
            int id;
            if (_urls.TryGetValue(link.Key, out var entry))
            {
                id = entry.Id;
                Rediscover(link, entry, depth, skip, exemptFromDepthLimit: true);
            }
            else
            {
                id = Insert(link, depth, skip, command.Source, null, exemptFromDepthLimit: true);
            }

            if (command.SitemapId is { } sitemapId)
            {
                _statements.InsertSitemapUrl(sitemapId, id);
            }
        }
    }

    private FrontierClaim ApplyClaim(int internalCount, int externalCount)
    {
        List<FrontierItem> internalItems = internalCount > 0 ? _statements.ClaimQueued(true, internalCount) : [];
        List<FrontierItem> externalItems = externalCount > 0 ? _statements.ClaimQueued(false, externalCount) : [];

        foreach (var item in internalItems.Concat(externalItems))
        {
            SetState(item.Url, UrlState.Fetching);
        }

        var claimed = internalItems.Count + externalItems.Count;
        _inFlight += claimed;
        _counters.OnClaimed(claimed);
        return new FrontierClaim(internalItems, externalItems, _inFlight);
    }

    private int ResolveLink(OutLink link, FrontierItem source, bool pageNoFollow)
    {
        var depth = link.Kind.KeepsDepth() ? source.Depth : CrawlDepth.Next(source.Depth);
        var skip = FollowDecision(link, pageNoFollow);

        if (_urls.TryGetValue(link.Key, out var entry))
        {
            Rediscover(link, entry, depth, skip, exemptFromDepthLimit: false);
            return entry.Id;
        }

        return Insert(link, depth, skip, DiscoverySource.Link, source.Id, exemptFromDepthLimit: false);
    }

    /// <summary>
    /// A known URL found again, so that what is stored for it doesn't depend on the order links and sitemaps turn up
    /// in. Any link can shorten its depth. A followed link or a sitemap listing can queue it or promote it to a full
    /// crawl; a link that isn't followed only leaves the strongest reason for skipping it.
    /// </summary>
    /// <param name="skip">Why this discovery alone would not be fetched, or None.</param>
    private void Rediscover(OutLink link, UrlEntry entry, int depth, SkipReason skip, bool exemptFromDepthLimit)
    {
        if (depth < entry.Depth)
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
                updated = skip == SkipReason.None ? Requeue(entry, mode)
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
    /// the links arrive in. A URL that was too deep to crawl and now isn't is queued.
    /// </summary>
    private UrlEntry LowerDepth(UInt128 key, UrlEntry entry, int depth, bool isInternal)
    {
        _statements.UpdateUrlDepth(entry.Id, depth);
        var updated = entry with { Depth = depth };
        if (updated is { State: UrlState.Skipped, Skip: SkipReason.DepthLimit })
        {
            // Only a followed page link is ever too deep, and it still leads here, now by a shorter path.
            var skip = Accept(isInternal, FetchMode.Crawl, depth, exemptFromDepthLimit: false);
            updated = skip == SkipReason.None ? Requeue(updated, FetchMode.Crawl)
                : skip > updated.Skip ? SetSkip(updated, skip, FetchMode.Crawl)
                : updated;
        }

        _urls[key] = updated;
        return updated;
    }

    /// <summary>
    /// A URL already status-checked that can now be crawled. A full fetch only differs when the check succeeded (a
    /// page's body, the links in its headers), so only then is it queued again; otherwise the check stands for it.
    /// </summary>
    private UrlEntry PromoteChecked(UrlEntry entry, bool isInternal)
    {
        if (_statements.ReadOutcome(entry.Id) is { Outcome: FetchOutcome.Success } check)
        {
            _statements.DeleteResponse(entry.Id);
            _statements.RequeueUrl(entry.Id, FetchMode.Crawl, entry.Depth);
            _counters.OnResponseRemoved(check.Outcome, isInternal && check.IsHtml, check.Bytes);
            return entry with { State = UrlState.Queued, Mode = FetchMode.Crawl };
        }

        return SetMode(entry, FetchMode.Crawl);
    }

    private UrlEntry Requeue(UrlEntry entry, FetchMode mode)
    {
        _statements.RequeueUrl(entry.Id, mode, entry.Depth);
        _counters.OnUrlRequeued();
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
        if (skip == SkipReason.None)
        {
            skip = Accept(link.IsInternal, mode, depth, exemptFromDepthLimit);
        }

        var state = skip == SkipReason.None ? UrlState.Queued : UrlState.Skipped;
        var id = _nextUrlId++;
        _statements.InsertUrl(id, link, mode, depth, state, skip, source, foundOnId);
        _urls[link.Key] = new UrlEntry(id, depth, state, skip, mode);
        _counters.OnUrlAdded(state == UrlState.Queued);
        return id;
    }

    /// <summary>Applies the depth and URL limits to a URL about to be queued. Status checks have no depth limit.</summary>
    private SkipReason Accept(bool isInternal, FetchMode mode, int depth, bool exemptFromDepthLimit)
    {
        if (mode == FetchMode.Crawl && !WithinDepthLimit(depth, exemptFromDepthLimit))
        {
            return SkipReason.DepthLimit;
        }

        if (isInternal)
        {
            if (_options.MaxUrls > 0 && _acceptedInternal >= _options.MaxUrls)
            {
                return SkipReason.UrlLimit;
            }

            _acceptedInternal++;
        }

        return SkipReason.None;
    }

    private bool WithinDepthLimit(int depth, bool exemptFromDepthLimit) =>
        exemptFromDepthLimit || _options.MaxDepth == 0 || depth <= _options.MaxDepth || depth >= CrawlDepth.UNLINKED;

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
        command.Parameters.AddWithValue("$options", JsonSerializer.Serialize(_options, EngineJsonContext.Default.CrawlOptions));
        command.Parameters.AddWithValue("$status", (int)CrawlStatus.Running);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$version", EngineInfo.Version);
        command.ExecuteNonQuery();
    }

    private void RestoreState()
    {
        // In one transaction: URLs claimed but never recorded when the last run stopped go back on the queue, and the
        // crawl is marked running again. A status of Running left by a crash is fine; the lock says nothing else writes.
        using (var transaction = _connection.BeginTransaction())
        {
            _statements.SetTransaction(transaction);
            CrawlDatabase.Execute(_connection, "UPDATE urls SET state = 0 WHERE state = 1;", transaction);
            _statements.UpdateCrawlStatus(CrawlStatus.Running, false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            transaction.Commit();
        }

        _statements.SetTransaction(null);
        SitemapsDone = Convert.ToInt64(CrawlDatabase.Scalar(_connection, "SELECT sitemaps_done FROM crawl WHERE id = 1")) == 1;

        long queued = 0;
        long skipped = 0;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT id, url, depth, state, skip_reason, fetch_mode, is_internal FROM urls";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new UrlEntry(reader.GetInt32(0), reader.GetInt32(2), (UrlState)reader.GetInt32(3),
                    (SkipReason)reader.GetInt32(4), (FetchMode)reader.GetInt32(5));
                _urls[UrlKey.Compute(reader.GetString(1))] = entry;
                _nextUrlId = Math.Max(_nextUrlId, entry.Id + 1);

                if (entry.State == UrlState.Skipped)
                {
                    skipped++;
                    continue;
                }

                if (entry.State == UrlState.Queued)
                {
                    queued++;
                }

                if (reader.GetBoolean(6))
                {
                    _acceptedInternal++;
                }
            }
        }

        _counters.RestoreUrls(_urls.Count, queued, skipped);

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT r.outcome, COUNT(*),
                       SUM(CASE WHEN r.is_html = 1 AND r.outcome = 0 AND u.is_internal = 1 THEN 1 ELSE 0 END),
                       COALESCE(SUM(r.size_bytes), 0)
                FROM responses r JOIN urls u ON u.id = r.url_id
                GROUP BY r.outcome
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                _counters.AddResponses((FetchOutcome)reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
            }
        }

        _logger.LogInformation("Resuming crawl with {Known} known URLs, {Queued} still queued", _urls.Count, queued);
    }

    private void BeginTransaction()
    {
        _transaction = _connection.BeginTransaction();
        _statements.SetTransaction(_transaction);
    }

    private void CommitTransaction()
    {
        _transaction!.Commit();
        _transaction.Dispose();
        _transaction = null;
        _statements.SetTransaction(null);
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
