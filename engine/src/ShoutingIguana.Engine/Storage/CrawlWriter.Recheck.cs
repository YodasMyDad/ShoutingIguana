using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Engine.Urls;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The writer in a recheck's run (the fix loop's "Recheck these N pages"): the crawl file opened as a continue opens it,
/// to fetch some of its URLs again and store each over its old record, never touching the crawl's queue or its status.
/// <list type="bullet">
/// <item>Opening brings the file up to this engine's schema (<see cref="CrawlSchemaUpgrade"/>), then begins the one
/// transaction the whole recheck is stored in: its "before" (the check's issues and targets on its URLs, its count, the
/// score) and the crawl row's stamp, so the analysis is out of date once it's stored, until the one after it.</item>
/// <item>Only the recheck's own URLs are handed out: its scope, the redirects they lead to (followed to their end), and
/// URLs the crawl didn't have that its scope's pages now link to, fetched one step. What those link to that the crawl
/// doesn't have is recorded as skipped (<see cref="SkipReason.Recheck"/>), and nothing else is queued.</item>
/// <item>A URL fetched again has its response, details, HTML and outgoing links replaced in the batch that stores it.</item>
/// <item>At the end, URLs nothing the crawl still has leads to are let go (<see cref="RecheckLetGo"/>), the recheck row is
/// ended with how its run ended and the requests it sent, each URL it judges with how the site answered it, and it all
/// commits at once.</item>
/// </list>
/// A recheck is all or nothing: one that fails anywhere (the file, the network, the run) is rolled back whole, so the
/// crawl file is as it was before it, its analysis still current, and no recheck is recorded. One stopped keeps what it
/// fetched. A process that dies part-way leaves the file as it was, too.
/// </summary>
internal sealed partial class CrawlWriter
{
    private RecheckRun? _recheck;

    /// <summary>The recheck's progress, or null for a crawl's run. Read from any thread.</summary>
    public RecheckProgress? RecheckProgress => _recheck?.Progress;

    /// <summary>The recheck's number in the file, or null for a crawl's run.</summary>
    public int? RecheckId => _recheck?.Id;

    /// <summary>
    /// Locks the crawl file and opens it for a recheck: the file is brought up to this engine's schema, its URLs are read
    /// back as a continue reads them, and the recheck's transaction begins with its "before" and the crawl row's stamp, its
    /// status left as it was. Then starts the write loop.
    /// </summary>
    /// <param name="atStage">Tests only: called at each <see cref="RecheckStage"/>, to fail the recheck there.</param>
    /// <exception cref="InvalidOperationException">
    /// The file is in use, isn't a crawl this engine can read, or was interrupted (it must be continued first).
    /// </exception>
    public static CrawlWriter OpenForRecheck(string databasePath, CrawlOptions options, RecheckPlan plan, CrawlCounters counters, ILogger logger,
        Action<RecheckStage>? atStage = null)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Crawl database not found.", databasePath);
        }

        var fileLock = CrawlFileLock.Acquire(databasePath);
        SqliteConnection? connection = null;
        try
        {
            connection = CrawlDatabase.OpenWriter(databasePath, allowNew: false);
            if (CrawlDatabase.EnsureSchema(connection))
            {
                throw new InvalidOperationException($"'{databasePath}' holds no crawl to recheck.");
            }

            if (CrawlDatabase.ReadStatus(connection) is CrawlStatus.Running or CrawlStatus.Paused)
            {
                throw new InvalidOperationException("This crawl was interrupted. Continue it (or stop it) before rechecking it.");
            }

            CrawlSchemaUpgrade.Apply(connection, databasePath);

            // A recheck never fetches the site's icon: the crawl has it, or found none.
            var writer = new CrawlWriter(connection, fileLock, options, counters, logger) { _recheck = new RecheckRun(plan), _hasFavicon = true, _atStage = atStage };
            var earlier = CrawlRunReader.Read(connection, transaction: null);
            writer.RestoreForRecheck(earlier);
            writer.BeginRecheck();
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
    /// The recheck's first phase is over: hands out the pages referring to the judged URLs that still fail, and returns how
    /// many (0 when there are none, and the recheck's fetching is done).
    /// </summary>
    public async Task<int> NextRecheckPhaseAsync(CancellationToken cancellationToken)
    {
        var reply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new NextRecheckPhaseCommand(reply), cancellationToken).ConfigureAwait(false);
        return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the crawl's URLs back as a continue does, without putting anything back on its queue or marking it running:
    /// a URL claimed and never stored when its last run stopped stays as it was, for that run's continue. Then begins the
    /// recheck's transaction, and in it stamps the crawl row, as every run does, and takes up the time the file keeps.
    /// </summary>
    /// <param name="earlier">The time the file knows of, read before the stamp: null when a run so far went untimed.</param>
    private void RestoreForRecheck(CrawlRuns? earlier)
    {
        RestoreKnownUrls();
        BeginTransaction();
        CrawlDatabase.Execute(_connection, $"UPDATE crawl SET updated_at = MAX({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}, updated_at + 1) WHERE id = 1", _transaction);
        if (earlier is not null)
        {
            PassStampOn(_transaction);
        }

        RestoreRunTime(earlier, _transaction);
    }

    /// <summary>
    /// Stores the recheck and its "before", in its transaction: its row, the URLs it judges and names as they stood (flagged
    /// or not, the issue's detail and other URL), the check's targets on them; then hands out its first phase, the URLs the
    /// crawl fetched (a URL it never fetched, a nofollow link's say, stays as it is).
    /// </summary>
    private void BeginRecheck()
    {
        var recheck = _recheck!;
        var plan = recheck.Plan;
        recheck.StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var transaction = _transaction;
        recheck.Id = Convert.ToInt32(RunTime("SELECT COALESCE(MAX(id), 0) + 1 FROM rechecks", transaction), System.Globalization.CultureInfo.InvariantCulture);
        RunTime("""
            INSERT INTO rechecks (id, run, started_at, status, check_id, group_detail, group_related_url_id, url_list, kinds, judged, count_before, score_before)
            VALUES ($id, $run, $startedAt, $status, $check, $detail, $related, $urlList, $kinds, $judged, $countBefore, (SELECT score FROM analysis WHERE id = 1))
            """, transaction, ("$id", recheck.Id), ("$run", _run), ("$startedAt", recheck.StartedAt), ("$status", (int)RecheckStatus.Running),
            ("$check", (object?)plan.CheckId ?? DBNull.Value), ("$detail", (object?)plan.Group?.Detail ?? DBNull.Value),
            ("$related", (object?)plan.Group?.RelatedUrlId ?? DBNull.Value), ("$urlList", plan.UrlList ? 1 : 0), ("$kinds", string.Join(",", plan.Kinds)),
            ("$judged", plan.Judged), ("$countBefore", (object?)plan.CountBefore ?? DBNull.Value));

        // A check read again only by its sitemaps, robots.txt or the site's versions records its URLs, and fetches none of
        // them but the probes of the site's versions.
        var fetchesUrls = plan.Kinds.Contains(Analysis.Models.RecheckKind.Urls);
        foreach (var url in plan.Scope.Where(url => url.Phase == 1))
        {
            RecordScopeUrl(url, transaction);
            foreach (var target in url.Role == RecheckRole.Judged ? url.Targets : [])
            {
                RunTime("INSERT OR IGNORE INTO recheck_targets (recheck_id, url_id, related_url_id) VALUES ($id, $url, $related)", transaction,
                    ("$id", recheck.Id), ("$url", url.Id), ("$related", target));
            }

            if (fetchesUrls || url.Source == DiscoverySource.SiteVersion)
            {
                HandOut(url, step: 0);
            }
        }

        // A duplicate check's pages are judged by whether their own text changed: it's kept as it stood.
        if (RecheckDuplicates.TextOf(plan.CheckId) is { } text)
        {
            RunTime($"""
                UPDATE recheck_urls SET text_before = (SELECT {text} FROM responses r WHERE r.url_id = recheck_urls.url_id)
                WHERE recheck_id = $id
                """, transaction, ("$id", recheck.Id));
        }

        _logger.LogInformation("Rechecking {Phase1} URLs of the crawl ({Judged} judged), recheck {Id}", plan.Phase1, plan.Judged, recheck.Id);
    }

    /// <summary>Records a URL of the recheck's scope as it stood before.</summary>
    private void RecordScopeUrl(RecheckScopeUrl url, SqliteTransaction? transaction) =>
        RunTime("""
            INSERT OR IGNORE INTO recheck_urls (recheck_id, url_id, role, flagged_before, detail_before, related_before)
            VALUES ($id, $url, $role, $flagged, $detail, $related)
            """, transaction, ("$id", _recheck!.Id), ("$url", url.Id), ("$role", (int)url.Role), ("$flagged", url.FlaggedBefore ? 1 : 0),
            ("$detail", (object?)url.DetailBefore ?? DBNull.Value), ("$related", (object?)url.RelatedBefore ?? DBNull.Value));

    /// <summary>Hands a URL of the scope out, the way the crawl fetched it, if the crawl fetched it at all.</summary>
    private void HandOut(RecheckScopeUrl url, int step)
    {
        if (!_urls.TryGetValue(UrlKey.Compute(url.Url), out var entry) || entry.State != UrlState.Done)
        {
            return;
        }

        if (_recheck!.Enqueue(new FrontierItem(entry.Id, url.Url, entry.Depth, entry.Mode, url.IsInternal, url.Source), step))
        {
            _urls[UrlKey.Compute(url.Url)] = entry with { State = UrlState.Fetching };
        }
    }

    /// <summary>The recheck's claim: only its own URLs, never the crawl's queue.</summary>
    private FrontierClaim ApplyRecheckClaim(int internalCount, int externalCount)
    {
        var internalItems = internalCount > 0 ? _recheck!.Claim(true, internalCount) : [];
        var externalItems = externalCount > 0 ? _recheck!.Claim(false, externalCount) : [];
        var claimed = internalItems.Count + externalItems.Count;
        _inFlight += claimed;
        _counters.OnClaimed(claimed);
        return new FrontierClaim(internalItems, externalItems, _inFlight);
    }

    /// <summary>
    /// Stores a URL the recheck fetched over its old record, all in the batch under way: the old response, details, HTML
    /// and outgoing links go, the new ones come. A URL that lost a link here is noted for the let-go walk.
    /// </summary>
    private void ApplyRecheckRecord(PageRecord record)
    {
        var recheck = _recheck!;
        var item = record.Item;
        var key = UrlKey.Compute(item.Url);
        var response = record.Response;
        var entry = _urls[key];
        _inFlight--;

        HashSet<int> before = [];
        if (_statements.ReadOutcome(entry.Id) is { } old)
        {
            before = LinkTargets(entry.Id);
            _statements.DeleteResponse(entry.Id);
            _counters.OnResponseRemoved(new RecordedResponse(old.Outcome, old.StatusCode, item.IsInternal && old.IsPage, item.IsInternal && !entry.IsProbe,
                old.IsNotChecked, old.Bytes, entry.IsProbe));
        }

        var step = recheck.StepOf(entry.Id);
        int? redirectId = null;
        int? canonicalId = null;
        var outlinkCount = 0;
        HashSet<int> after = [];
        foreach (var link in record.Links)
        {
            var targetId = ResolveRecheckLink(link, item.Id, entry.Depth, record.PageNoFollow, entry.IsProbe, step);
            after.Add(targetId);
            _statements.InsertLink(item.Id, targetId, link);
            switch (link.Kind)
            {
                case LinkKind.Redirect:
                    redirectId ??= targetId;
                    break;
                case LinkKind.Canonical when (link.Flags & LinkFlags.OutsideHead) == 0:
                    canonicalId ??= targetId;
                    break;
                case LinkKind.Hyperlink:
                    outlinkCount++;
                    break;
            }
        }

        _atStage?.Invoke(RecheckStage.PageStoring);
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
        _counters.OnRecorded(new RecordedResponse(response.Outcome, response.StatusCode, IsPage(item.IsInternal, item.Url, response),
            IsSiteUrl: item.IsInternal && !entry.IsProbe, IsNotChecked(response), DownloadedBytes(response.SizeBytes, response.BodyBytes), entry.IsProbe));
        recheck.Recorded(item, response);

        // Links a page dropped are looked at for the let-go walk only when it was read for what it is now: a page, a
        // redirect, or one that's gone. A fetch that failed, was blocked or refused says nothing of where it links.
        if (response.Outcome is FetchOutcome.Success or FetchOutcome.Redirect or FetchOutcome.ClientError)
        {
            recheck.LostLinks.UnionWith(before.Where(target => !after.Contains(target)));
        }
        PropagateDepths();
    }

    /// <summary>
    /// A link on a page the recheck fetched. A URL the crawl has keeps its record (only its depth can drop), but a redirect is
    /// followed into it, once, as fetching the page goes on there. A URL it doesn't have is stored: fetched one step from
    /// the recheck's scope (or wherever a redirect leads, at the step of the URL redirecting), and recorded as skipped
    /// past that, for a full crawl to follow. A link the crawl's options don't follow is recorded as the crawl would.
    /// </summary>
    private int ResolveRecheckLink(OutLink link, int sourceId, int sourceDepth, bool pageNoFollow, bool sourceIsProbe, int step)
    {
        var depth = link.Kind.KeepsDepth() ? sourceDepth : CrawlDepth.Next(sourceDepth);
        if (sourceIsProbe)
        {
            return Probe(link, depth, sourceId);
        }

        var nextStep = link.Kind == LinkKind.Redirect ? step : step + 1;
        if (_urls.TryGetValue(link.Key, out var entry))
        {
            if (depth < entry.Depth && !entry.IsProbe)
            {
                entry = LowerDepth(link.Key, entry, depth, link.IsInternal);
            }

            if (link.Kind == LinkKind.Redirect && entry.State == UrlState.Done && !_recheck!.Fetched.Contains(entry.Id) &&
                _recheck.Enqueue(new FrontierItem(entry.Id, link.Url, entry.Depth, entry.Mode, link.IsInternal, DiscoverySource.Link), nextStep))
            {
                _urls[link.Key] = entry with { State = UrlState.Fetching };
            }

            return entry.Id;
        }

        var skip = FollowDecision(link, pageNoFollow);
        if (skip == SkipReason.None && nextStep > 1)
        {
            skip = SkipReason.Recheck;
        }

        var id = Insert(link, depth, skip, DiscoverySource.Link, sourceId, exemptFromDepthLimit: true);
        if (skip == SkipReason.None)
        {
            _recheck!.Enqueue(new FrontierItem(id, link.Url, depth, ModeFor(link), link.IsInternal, DiscoverySource.Link), nextStep);
            _recheck.Found.Add(id);
            _urls[link.Key] = _urls[link.Key] with { State = UrlState.Fetching };
            RunTime("INSERT OR IGNORE INTO recheck_urls (recheck_id, url_id, role, flagged_before) VALUES ($id, $url, $role, 0)", _transaction,
                ("$id", _recheck.Id), ("$url", id), ("$role", (int)RecheckRole.New));
        }

        return id;
    }

    /// <summary>The second phase: the referring pages of the judged URLs still failing, recorded as they stood and handed out.</summary>
    private int ApplyNextRecheckPhase()
    {
        var referring = _recheck!.StartPhaseTwo();
        var handedOut = 0;
        foreach (var url in referring)
        {
            var known = _urls.TryGetValue(UrlKey.Compute(url.Url), out var entry) ? entry : default;
            if (known.State != UrlState.Done)
            {
                continue;
            }

            RecordScopeUrl(url, _transaction);
            HandOut(url, step: 0);
            handedOut++;
        }

        _logger.LogInformation("Recheck {Id}: {Count} pages referring to URLs that still fail", _recheck.Id, handedOut);
        return handedOut;
    }

    /// <summary>
    /// The run is over. One that failed is rolled back whole: the file is as it was before the recheck, and nothing more
    /// is stored. Otherwise the URLs nothing leads to any more are let go, the crawl row is stamped (its status as it was),
    /// each URL judged gets how the site answered it, and the recheck row is ended with how the run ended and the requests
    /// it sent; the batch under way then commits it all.
    /// </summary>
    /// <param name="requests">The requests the run sent the site: its pages, robots.txt and sitemaps.</param>
    private void EndRecheck(CrawlStatus status, long requests)
    {
        var recheck = _recheck!;
        recheck.Ended = true;
        if (status == CrawlStatus.Failed)
        {
            DiscardRecheck();
            return;
        }

        foreach (var url in recheck.Plan.Scope.Where(url => url.Role == RecheckRole.Judged))
        {
            RunTime("UPDATE recheck_urls SET answer = $answer WHERE recheck_id = $id AND url_id = $url", _transaction,
                ("$answer", (int)recheck.AnswerOf(url.Id)), ("$id", recheck.Id), ("$url", url.Id));
        }

        // A URL it found and didn't get to (it was stopped) is recorded as found by it, not queued: the crawl's queue is as it was.
        foreach (var id in recheck.Found.Where(id => !recheck.Fetched.Contains(id)))
        {
            var url = (string)RunTime("SELECT url FROM urls WHERE id = $id", _transaction, ("$id", id))!;
            var entry = _urls[UrlKey.Compute(url)];
            _statements.UpdateUrlSkip(id, SkipReason.Recheck, entry.Mode);
            _statements.UpdateUrlState(id, UrlState.Skipped);
            _urls[UrlKey.Compute(url)] = entry with { State = UrlState.Skipped, Skip = SkipReason.Recheck };
            _counters.OnLetGo(null, false, wasCounted: true);
        }

        foreach (var id in RecheckLetGo.Find(_connection, _transaction, recheck.LostLinks))
        {
            LetGo(id);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RunTime("UPDATE crawl SET updated_at = MAX($now, updated_at + 1) WHERE id = 1", _transaction, ("$now", now));
        _crawlStamped = true;
        var ended = status == CrawlStatus.Completed ? RecheckStatus.Completed : RecheckStatus.Stopped;
        RunTime("UPDATE rechecks SET ended_at = $now, status = $status, fetched = $fetched WHERE id = $id", _transaction,
            ("$now", now), ("$status", (int)ended), ("$fetched", requests), ("$id", recheck.Id));
    }

    /// <summary>
    /// The recheck failed: everything it stored is rolled back, and whatever reaches the writer from here on (its last
    /// notices, the time it spent) is let go, so the file is as it was before it.
    /// </summary>
    private void DiscardRecheck()
    {
        var recheck = _recheck!;
        if (recheck.Discarded)
        {
            return;
        }

        recheck.Ended = true;
        recheck.Discarded = true;
        RollBackTransaction();
        _notices.Clear();
        _runTime.Clear();
        _crawlStamped = false;
        _logger.LogWarning("Recheck {Id} failed: nothing it fetched was stored, and the crawl file is as it was", recheck.Id);
    }

    /// <summary>
    /// Lets a URL go: its response, details, HTML and outgoing links are deleted, and it is skipped as no longer linked. The
    /// row stays, so ids stay stable.
    /// </summary>
    private void LetGo(int id)
    {
        var url = (string)RunTime("SELECT url FROM urls WHERE id = $id", _transaction, ("$id", id))!;
        var key = UrlKey.Compute(url);
        var entry = _urls[key];
        var isInternal = Convert.ToInt64(RunTime("SELECT is_internal FROM urls WHERE id = $id", _transaction, ("$id", id)), System.Globalization.CultureInfo.InvariantCulture) == 1;
        RecordedResponse? stored = _statements.ReadOutcome(id) is { } old
            ? new RecordedResponse(old.Outcome, old.StatusCode, isInternal && old.IsPage, isInternal && !entry.IsProbe, old.IsNotChecked, old.Bytes, entry.IsProbe)
            : null;
        _statements.DeleteResponse(id);
        _statements.UpdateUrlSkip(id, SkipReason.NoLongerLinked, entry.Mode);
        _statements.UpdateUrlState(id, UrlState.Skipped);
        _urls[key] = entry with { State = UrlState.Skipped, Skip = SkipReason.NoLongerLinked };
        _counters.OnLetGo(stored, isInternal && !entry.IsProbe, wasCounted: entry.State != UrlState.Skipped);
        RunTime($"""
            INSERT OR IGNORE INTO recheck_urls (recheck_id, url_id, role, flagged_before)
            VALUES ($id, $url, {(int)RecheckRole.LetGo}, EXISTS (SELECT 1 FROM issues WHERE url_id = $url AND issue = $check))
            """, _transaction, ("$id", _recheck!.Id), ("$url", id), ("$check", (object?)_recheck.Plan.CheckId ?? DBNull.Value));
    }

    /// <summary>
    /// A sitemap the recheck reads again (the sitemaps kind): its entries as read now replace those read before, which go
    /// as it is registered, before its URLs come.
    /// </summary>
    private int RegisterRecheckSitemap(string url, int? parentId)
    {
        var id = _statements.RegisterSitemap(url, parentId);
        if (_recheck!.SitemapsRead.Add(id))
        {
            RunTime("DELETE FROM sitemap_urls WHERE sitemap_id = $id", _transaction, ("$id", id));
            _atStage?.Invoke(RecheckStage.SitemapCleared);
        }

        return id;
    }

    /// <summary>
    /// The sitemaps were read again in full: one the crawl had that nothing lists any more (a file dropped from the index)
    /// is no sitemap of the site's now, and goes, with its entries.
    /// </summary>
    private void DropSitemapsNotReadAgain()
    {
        var read = _recheck!.SitemapsRead;
        if (read.Count == 0)
        {
            return;
        }

        var kept = string.Join(", ", read);
        RunTime($"DELETE FROM sitemap_urls WHERE sitemap_id NOT IN ({kept})", _transaction);
        RunTime($"DELETE FROM sitemaps WHERE id NOT IN ({kept})", _transaction);
    }

    /// <summary>
    /// URLs a sitemap read again lists: one the crawl has is listed, as read now; one it doesn't have is recorded as found by
    /// the recheck and not fetched, for a full crawl to read.
    /// </summary>
    private void ApplyRecheckDiscover(DiscoverCommand command)
    {
        for (var index = 0; index < command.Urls.Count; index++)
        {
            var link = command.Urls[index];
            var id = _urls.TryGetValue(link.Key, out var entry)
                ? entry.Id
                : Insert(link, CrawlDepth.UNLINKED, SkipReason.Recheck, command.Source, null, exemptFromDepthLimit: true);
            if (command.SitemapId is { } sitemapId)
            {
                // With its lastmod as read now, as a crawl keeps it (sitemap-lastmod-invalid reads it).
                _statements.InsertSitemapUrl(sitemapId, id, command.Lastmods is { } lastmods && index < lastmods.Count ? lastmods[index] : null);
            }
        }
    }

    /// <summary>Where a stored URL's links go, by id.</summary>
    private HashSet<int> LinkTargets(int fromId)
    {
        HashSet<int> targets = [];
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "SELECT to_id FROM links WHERE from_id = $id";
        command.Parameters.AddWithValue("$id", fromId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            targets.Add(reader.GetInt32(0));
        }

        return targets;
    }
}
