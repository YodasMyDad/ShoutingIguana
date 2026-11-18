using System.Diagnostics;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Library;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// Rechecks (the fix loop's "Recheck these N pages"): some of a crawl's URLs fetched again into the same file, then the
/// crawl analysed, which settles the recheck. A recheck is live as a crawl is: one writer at a time, and no other run of
/// the crawl's site here meanwhile, as each run paces its hosts alone. Its flow: the analysis first when it is out of
/// date (so the "before" it records is today's), the recheck's run, then the analysis. A run that failed stored nothing
/// (the engine rolls it back whole), so there is nothing to analyse: the crawl ends with why.
/// </summary>
internal sealed partial class CrawlManager
{
    private const string RECHECKING_HERE = "This crawl is being rechecked. Wait for that to finish, or stop it.";

    /// <summary>What a recheck refused for another run of its site here says.</summary>
    private const string SITE_RUNNING = "A check of this site is running. Its results will show what’s fixed.";

    /// <summary>What a crawl refused for a recheck of its site here says.</summary>
    private const string SITE_RECHECKED = "This site is being rechecked. Start the check once that’s done.";

    /// <summary>Tests only: starts a recheck's run in place of the engine.</summary>
    internal Func<RecheckRequest, ICrawlObserver, CrawlJob>? RecheckJob { get; set; }

    /// <summary>What a recheck would do, the secrets it would ask for, and why it can't run now if it can't. Nothing is written.</summary>
    public RecheckPlanDto PlanRecheck(string id, RecheckAskRequest body)
    {
        var (path, ask) = Ask(id, body);
        var plan = Plan(path, ask);
        var resume = EngineErrors.Read(() => CrawlDatabase.ReadOptions(path)).PlanResume(new ResumeChanges());
        var refused = Refusal(id, plan)?.Problem;
        return RecheckPlanDto.From(plan, new RecheckSecretsDto(resume.MissingHeaders, resume.MissingProxyCredentials),
            refused is null ? null : new RecheckRefusalDto(refused.Type, refused.Title));
    }

    /// <summary>
    /// Starts a recheck: what the plan refuses, and the secrets Continue would ask for, are problems; otherwise the crawl is
    /// live from now until the analysis after the recheck has settled it. Answers with the plan and the recheck's number.
    /// </summary>
    public RecheckPlanDto Recheck(string id, StartRecheckRequest body)
    {
        lock (_sync)
        {
            ThrowIfStopping();
        }

        var (path, ask) = Ask(id, body);
        var plan = Plan(path, ask);
        if (Refusal(id, plan) is { } refused)
        {
            throw refused;
        }

        var headers = CrawlOptionsDto.Headers(body.ExtraHeaders, out var headerErrors);
        var resume = EngineErrors.Read(() => CrawlDatabase.ReadOptions(path)).PlanResume(new ResumeChanges { ExtraHeaders = headers, ProxyUrl = body.ProxyUrl });
        var missingHeaders = body.SkipMissingHeaders ? [] : resume.MissingHeaders;
        if (resume.MissingProxyCredentials || missingHeaders.Count > 0)
        {
            throw Problems.NeedsSecrets(missingHeaders, resume.MissingProxyCredentials, "Rechecking");
        }

        var errors = OptionFields.Merge(headerErrors, resume.Options.ValidateFields());
        if (errors.Count > 0)
        {
            throw Problems.InvalidOptions(errors);
        }

        // Its number as the run will store it: the next in the file, which nothing else can take while it's live here.
        var recheckId = (EngineErrors.Read(() => RecheckReader.ReadAll(path)).FirstOrDefault()?.Id ?? 0) + 1;
        var site = PreviousCrawls.SiteKeyOf(resume.Options.StartUrl);
        lock (_sync)
        {
            // Something may have started meanwhile, here.
            ThrowIfStopping();
            ThrowIfLive(id);
            if (SiteRunning(site, id))
            {
                throw Problems.SiteBusy(SITE_RUNNING);
            }

            var first = plan.AnalysisFirst;
            var crawl = new LiveCrawl(id, path, resume.Options.StartUrl)
            {
                State = first ? LiveCrawlState.Analyzing : LiveCrawlState.Rechecking,
                Analysis = first ? new CancellationTokenSource() : null,
                Recheck = new LiveRecheck(recheckId)
            };
            _crawls[id] = crawl;
            PublishCrawl(new CrawlEventDto(id, first ? CrawlListStatus.Analyzing : CrawlListStatus.Rechecking), crawlEnded: false);
            crawl.Lifetime = Task.Run(() => RecheckFlowAsync(crawl, ask, plan, resume.Options));
        }

        events.PublishLibrary();
        return RecheckPlanDto.From(plan, new RecheckSecretsDto(missingHeaders, false), null) with { RecheckId = recheckId };
    }

    /// <summary>The analysis first when it's out of date, then the recheck's run and the analysis after it.</summary>
    private async Task RecheckFlowAsync(LiveCrawl crawl, RecheckAsk ask, RecheckPlan plan, CrawlOptions options)
    {
        try
        {
            if (plan.AnalysisFirst)
            {
                var (error, errorType, completed) = await RunAnalysisAsync(crawl).ConfigureAwait(false);
                if (!completed)
                {
                    End(crawl, error, errorType);
                    return;
                }

                // Planned again from today's analysis: the "before" the recheck records is of it.
                plan = Plan(crawl.Path, ask);
                bool stop;
                lock (_sync)
                {
                    stop = crawl.StopRequested || _stopping || plan.Refused is not null;
                    if (!stop)
                    {
                        crawl.State = LiveCrawlState.Rechecking;
                        crawl.Analysis = null;
                        PublishCrawl(new CrawlEventDto(crawl.Id, CrawlListStatus.Rechecking), crawlEnded: false);
                    }
                }

                if (stop)
                {
                    End(crawl, plan.Refused?.Title);
                    return;
                }
            }

            await RecheckRunAsync(crawl, plan, options).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Nothing awaits this task but the shutdown, so whatever went wrong is logged here.
            _logger.LogError(exception, "The recheck of {Path} failed", crawl.Path);
            End(crawl, exception is ProblemException problem ? problem.Problem.Title : $"The recheck failed: {exception.Message}");
        }
    }

    /// <summary>The recheck's run, then the analysis that settles it, its events on the way.</summary>
    private async Task RecheckRunAsync(LiveCrawl crawl, RecheckPlan plan, CrawlOptions options)
    {
        // The file has every alert of the runs before this one.
        alerts.Forget(crawl.Id);
        CrawlJob job;
        try
        {
            job = (RecheckJob ?? _engine.Recheck)(new RecheckRequest(crawl.Path, plan, options), new SidecarCrawlObserver(this, crawl));
        }
        catch (Exception exception)
        {
            var problem = StartProblem(exception, new CrawlRequest(options, crawl.Path, Resume: true)).Problem;
            End(crawl, problem.Title, problem.Type == "crawl-busy" ? problem.Type : null);
            return;
        }

        bool stopNow;
        lock (_sync)
        {
            crawl.Job = job;
            crawl.Recheck!.Started = true;
            stopNow = crawl.StopRequested;
            PublishRecheck(crawl, RecheckStatus.Running, job.Progress.Recheck, result: null, force: true);
            PublishProgress(crawl, job.Progress);
        }

        _logger.LogInformation("Rechecking {Judged} URLs of {Path}", plan.Judged, crawl.Path);
        if (stopNow)
        {
            _ = job.StopAsync();
        }

        CrawlSummary summary;
        try
        {
            summary = await job.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The recheck of {Path} failed", crawl.Path);
            summary = new CrawlSummary(CrawlStatus.Failed, job.Progress, crawl.Path, exception.Message);
        }
        finally
        {
            await job.DisposeAsync().ConfigureAwait(false);
            summaries.Invalidate(crawl.Id);
            _histories.Invalidate(crawl.Id);
            alerts.Forget(crawl.Id);
        }

        var recheckId = summary.RecheckId ?? crawl.Recheck.Id;
        var ran = ReadRecheck(crawl.Path, recheckId);
        bool analyse;
        lock (_sync)
        {
            crawl.Recheck.Id = recheckId;
            crawl.Job = null;
            PublishRecheck(crawl, ran?.Status ?? (summary.Status == CrawlStatus.Failed ? RecheckStatus.Failed : RecheckStatus.Stopped), summary.Progress.Recheck, result: null,
                force: true);

            // Not when the app is shutting down: the next analysis settles it. Nor after a run that failed, which stored nothing.
            analyse = !_stopping && summary.Status != CrawlStatus.Failed;
            if (analyse)
            {
                crawl.State = LiveCrawlState.Analyzing;
                crawl.Analysis = new CancellationTokenSource();
                PublishCrawl(new CrawlEventDto(crawl.Id, CrawlListStatus.Analyzing), crawlEnded: false);
            }
        }

        _logger.LogInformation("The recheck of {Path} {Status}: {Fetched} requests", crawl.Path, summary.Status, ran?.Fetched);
        var failed = summary.Status == CrawlStatus.Failed ? $"The recheck failed: {summary.Error}" : null;
        if (!analyse)
        {
            End(crawl, failed);
            return;
        }

        var (error, errorType, _) = await RunAnalysisAsync(crawl).ConfigureAwait(false);
        if (ReadRecheck(crawl.Path, recheckId) is { Result: { } result } settled)
        {
            lock (_sync)
            {
                PublishRecheck(crawl, settled.Status, summary.Progress.Recheck, result, force: true);
            }
        }

        End(crawl, error ?? failed, errorType);
    }

    /// <summary>
    /// A recheck's event. Called under the lock: at most every 500 ms while it fetches, and always as it starts, as its run
    /// ends, and once it is settled.
    /// </summary>
    private void PublishRecheck(LiveCrawl crawl, RecheckStatus status, RecheckProgress? progress, RecheckResult? result, bool force)
    {
        Debug.Assert(_sync.IsHeldByCurrentThread);
        var recheck = crawl.Recheck!;
        var now = DateTime.UtcNow;
        if (!force && now - recheck.LastEventAt < RECHECK_EVENT_INTERVAL)
        {
            return;
        }

        recheck.LastEventAt = now;
        var dto = new RecheckEventDto(crawl.Id, recheck.Id, status, progress?.Phase, progress?.Done ?? 0, progress?.Total ?? 0, result);
        Publishing?.Invoke(dto);
        events.PublishRecheck(dto);
    }

    private static readonly TimeSpan RECHECK_EVENT_INTERVAL = TimeSpan.FromMilliseconds(500);

    /// <summary>One recheck as the file keeps it; null when it can't be read (the file went), which the events then do without.</summary>
    private RecheckRecord? ReadRecheck(string path, int id)
    {
        try
        {
            return RecheckReader.Read(path, id);
        }
        catch (Exception exception) when (EngineErrors.IsExpected(exception))
        {
            _logger.LogWarning("Could not read recheck {Id} of {Path}: {Error}", id, path, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The crawl a recheck is asked of, and what it is asked to settle. A check, or a list of URLs (1 to 1,000, with the check
    /// to judge them by or none); a group is one of the check's, without a list. What can't be is <c>invalid-query</c>.
    /// </summary>
    private (string Path, RecheckAsk Ask) Ask(string id, RecheckAskRequest body)
    {
        var path = PathOf(id);
        Dictionary<string, IReadOnlyList<string>> errors = new(StringComparer.Ordinal);
        if (body.CheckId is null && body.UrlIds is null)
        {
            errors["checkId"] = ["Give the check to recheck (checkId), or the URLs (urlIds)."];
        }

        if (body.UrlIds is { Count: 0 or > RecheckPlanReader.LIMIT })
        {
            errors["urlIds"] = [$"Give from 1 to {RecheckPlanReader.LIMIT:N0} URL ids."];
        }

        if (body.Group is not null && (body.CheckId is null || body.UrlIds is not null))
        {
            errors["group"] = ["A group is one of a check's: give it with its check, and without urlIds."];
        }

        if (errors.Count > 0)
        {
            throw Problems.InvalidQuery(errors);
        }

        if (body.CheckId is { } checkId && IssueCatalog.Find(checkId) is null)
        {
            throw Problems.CheckNotFound(checkId);
        }

        // An id no URL can have names a URL the crawl doesn't have, which the planner refuses.
        IReadOnlyList<int>? urlIds = body.UrlIds is { } ids ? [.. ids.Select(urlId => urlId is >= 1 and <= int.MaxValue ? (int)urlId : 0)] : null;
        return (path, new RecheckAsk(body.CheckId, body.Group is { } group ? new IssueGroupKey(group.Detail, group.RelatedUrlId) : null, urlIds));
    }

    /// <summary>The engine's plan, with the thresholds an analysis would run with now; an ask it refuses is <c>invalid-query</c>.</summary>
    private RecheckPlan Plan(string path, RecheckAsk ask)
    {
        try
        {
            return EngineErrors.Read(() => RecheckPlanReader.Plan(path, ask, AnalysisSettings));
        }
        catch (ArgumentException exception) when (exception.ParamName is "ask" or "judged")
        {
            throw Problems.InvalidQuery(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [ask.UrlIds is null ? "checkId" : "urlIds"] = [exception.Message.Split(" (Parameter", StringSplitOptions.None)[0]]
            });
        }
    }

    /// <summary>
    /// Why a recheck can't run now, as the problem starting it would answer: the crawl live here; then what the engine says
    /// of the file (in use elsewhere, interrupted, never analysed, a check a recheck can't settle); then the site's: another
    /// run of the site is live here, or the crawl isn't the site's latest. Null when it can run.
    /// </summary>
    private ProblemException? Refusal(string id, RecheckPlan plan)
    {
        lock (_sync)
        {
            if (_crawls.TryGetValue(id, out var live))
            {
                return Problems.CrawlBusy(LiveMessage(live));
            }
        }

        if (plan.Refused is { } refused)
        {
            return refused.Code switch
            {
                RecheckRefusalCode.CrawlBusy => Problems.CrawlBusy(refused.Title),
                RecheckRefusalCode.NotAnalyzed => Problems.NotAnalyzed(refused.Title),
                RecheckRefusalCode.NotAnalysable => Problems.NotAnalysable(refused.Title),
                _ => Problems.RecheckUnsupported(refused.Title)
            };
        }

        var (entry, entries) = EntryOf(id);
        if (entry.Item.SiteKey is not { } site)
        {
            return null;
        }

        // A check of the site running here comes first: it is likely the newer crawl, and its results will say what's fixed.
        lock (_sync)
        {
            if (SiteRunning(site, id))
            {
                return Problems.SiteBusy(SITE_RUNNING);
            }
        }

        var latest = entries.Where(other => other.Item.SiteKey == site && !other.Item.Missing)
            .OrderByDescending(other => other.Item.StartedAt ?? DateTime.MinValue)
            .ThenBy(other => other.Item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return latest is not null && latest.Item.Id != id ? Problems.RecheckNotLatest() : null;
    }

    /// <summary>
    /// Another run of the site is live here (a crawl, or a recheck in any of its steps): one that fetches from it, or soon
    /// will. An analysis alone fetches nothing. Called under the lock.
    /// </summary>
    private bool SiteRunning(string? site, string exceptId)
    {
        Debug.Assert(_sync.IsHeldByCurrentThread);
        return site is not null && _crawls.Values.Any(crawl => crawl.Id != exceptId && (crawl.Recheck is not null || crawl.State is not LiveCrawlState.Analyzing) &&
                                                               PreviousCrawls.SiteKeyOf(crawl.StartUrl) == site);
    }

    /// <summary>A crawl of a site can't start while one of the site's crawls is rechecked here. Called under the lock.</summary>
    private void ThrowIfSiteRechecked(string startUrl)
    {
        Debug.Assert(_sync.IsHeldByCurrentThread);
        var site = PreviousCrawls.SiteKeyOf(startUrl);
        if (site is not null && _crawls.Values.Any(crawl => crawl.Recheck is not null && PreviousCrawls.SiteKeyOf(crawl.StartUrl) == site))
        {
            throw Problems.SiteBusy(SITE_RECHECKED);
        }
    }

    /// <summary>What a crawl live here is doing, as a refusal says it.</summary>
    private static string LiveMessage(LiveCrawl crawl) =>
        crawl.Recheck is not null ? RECHECKING_HERE : crawl.State is LiveCrawlState.Analyzing ? ANALYSING_HERE : RUNNING_HERE;
}
