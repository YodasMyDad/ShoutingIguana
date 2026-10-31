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

    private CrawlWriter(SqliteConnection connection, CrawlOptions options, CrawlCounters counters, ILogger logger)
    {
        _connection = connection;
        _options = options;
        _counters = counters;
        _logger = logger;
        _statements = new WriterStatements(connection);
    }

    /// <summary>Completes when the write loop ends; faults if the database failed.</summary>
    public Task Completion => _loop;

    public bool SitemapsDone { get; private set; }

    /// <summary>Opens or creates the crawl database, restores a resumed crawl, and starts the write loop.</summary>
    public static CrawlWriter Open(string databasePath, CrawlOptions options, bool resume, CrawlCounters counters, ILogger logger)
    {
        var connection = CrawlDatabase.OpenWriter(databasePath);
        try
        {
            var created = CrawlDatabase.EnsureSchema(connection);
            var writer = new CrawlWriter(connection, options, counters, logger);
            if (created)
            {
                writer.InsertCrawlRow();
            }
            else if (!resume)
            {
                throw new InvalidOperationException($"'{databasePath}' already holds a crawl. Resume it or choose a new file.");
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
            connection.Dispose();
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

    /// <summary>Stops accepting commands, drains and commits what is queued, then folds the WAL into the file.</summary>
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
        }
        finally
        {
            _statements.Dispose();
            CrawlDatabase.CloseWriter(_connection, _logger);
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
                case LinkKind.Canonical:
                    canonicalId ??= targetId;
                    break;
                case LinkKind.Hyperlink:
                    outlinkCount++;
                    break;
            }
        }

        var response = record.Response;
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
        SetState(item.Url, UrlState.Done);
        _inFlight--;

        var isSitePage = item.IsInternal && response.IsHtml && response.Outcome == FetchOutcome.Success;
        _counters.OnRecorded(response.Outcome, isSitePage, response.SizeBytes ?? 0);
    }

    private void ApplyDiscover(DiscoverCommand command)
    {
        var depth = command.Source == DiscoverySource.Seed ? 0 : CrawlDepth.UNLINKED;

        foreach (var link in command.Urls)
        {
            int id;
            if (_urls.TryGetValue(link.Key, out var entry))
            {
                id = entry.Id;
                if (command.Source == DiscoverySource.Seed)
                {
                    Revisit(link, entry, depth);
                }
            }
            else
            {
                var skip = !link.IsInternal && !_options.CheckExternalLinks ? SkipReason.ExternalsDisabled : SkipReason.None;
                id = Insert(link, depth, skip, command.Source, null);
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
            if (skip == SkipReason.None)
            {
                Revisit(link, entry, depth);
            }

            return entry.Id;
        }

        return Insert(link, depth, skip, DiscoverySource.Link, source.Id);
    }

    /// <summary>
    /// A known URL found again through a followed link: keep its shortest depth, queue it if it was only
    /// skipped for a reason this link overrides, and upgrade a queued status check to a full crawl.
    /// </summary>
    private void Revisit(OutLink link, UrlEntry entry, int depth)
    {
        var updated = entry;
        if (depth < updated.Depth)
        {
            updated.Depth = depth;
            _statements.UpdateUrlDepth(updated.Id, depth);
        }

        if (updated.State == UrlState.Skipped &&
            updated.Skip is SkipReason.NoFollow or SkipReason.NotFollowedByOption or SkipReason.DepthLimit)
        {
            var mode = link.IsCrawlable ? FetchMode.Crawl : updated.Mode;
            var skip = Accept(link.IsInternal, mode, updated.Depth);
            if (skip == SkipReason.None)
            {
                updated = updated with { State = UrlState.Queued, Skip = SkipReason.None, Mode = mode };
                _statements.RequeueUrl(updated.Id, mode, updated.Depth);
                _counters.OnUrlRequeued();
            }
            else if (skip != updated.Skip)
            {
                updated.Skip = skip;
                _statements.UpdateUrlSkip(updated.Id, skip);
            }
        }
        else if (updated is { State: UrlState.Queued, Mode: FetchMode.Check } && link.IsCrawlable)
        {
            updated.Mode = FetchMode.Crawl;
            _statements.UpdateUrlMode(updated.Id, FetchMode.Crawl);
        }

        if (updated != entry)
        {
            _urls[link.Key] = updated;
        }
    }

    private int Insert(OutLink link, int depth, SkipReason skip, DiscoverySource source, int? foundOnId)
    {
        var mode = link.IsCrawlable ? FetchMode.Crawl : FetchMode.Check;
        if (skip == SkipReason.None)
        {
            skip = Accept(link.IsInternal, mode, depth);
        }

        var state = skip == SkipReason.None ? UrlState.Queued : UrlState.Skipped;
        var id = _nextUrlId++;
        _statements.InsertUrl(id, link, mode, depth, state, skip, source, foundOnId);
        _urls[link.Key] = new UrlEntry(id, depth, state, skip, mode);
        _counters.OnUrlAdded(state == UrlState.Queued);
        return id;
    }

    /// <summary>Applies the depth and URL limits to a URL about to be queued.</summary>
    private SkipReason Accept(bool isInternal, FetchMode mode, int depth)
    {
        if (mode == FetchMode.Crawl && _options.MaxDepth > 0 && depth > _options.MaxDepth && depth < CrawlDepth.UNLINKED)
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
        // URLs claimed but never recorded when the last run stopped go back on the queue.
        CrawlDatabase.Execute(_connection, "UPDATE urls SET state = 0 WHERE state = 1;");
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
