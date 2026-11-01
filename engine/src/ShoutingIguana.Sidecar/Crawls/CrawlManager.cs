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
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Events.Models;
using ShoutingIguana.Sidecar.Library;
using ShoutingIguana.Sidecar.Models;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// The crawls and analyses this process runs, and the library's view of every crawl. A crawl file has one writer at a
/// time: a crawl here, an analysis here, or something in another process (its lock says so). Every change of status
/// is published as a <c>crawl</c> event, and every change to what the library lists as a <c>library</c> event.
/// </summary>
internal sealed class CrawlManager(
    SidecarOptions options,
    CrawlLibrary library,
    CrawlSummaryCache summaries,
    EventHub events,
    AlertLog alerts,
    ILoggerFactory loggerFactory)
{
    private const string RUNNING_HERE = "This crawl is running. Stop it first.";
    private const string ANALYSING_HERE = "This crawl is being analysed. Wait for that to finish, or cancel it.";
    private const string IN_USE_ELSEWHERE = "Another program is writing to this crawl. Try again once it has finished.";
    private const string SHUTTING_DOWN = "The engine is shutting down.";

    private readonly Lock _sync = new();
    private readonly Dictionary<string, LiveCrawl> _crawls = new(StringComparer.Ordinal);
    private readonly CrawlEngine _engine = new(loggerFactory);
    private readonly ILogger<CrawlManager> _logger = loggerFactory.CreateLogger<CrawlManager>();
    private bool _stopping;

    /// <summary>Every crawl in the library, newest first.</summary>
    public IReadOnlyList<CrawlListItem> List()
    {
        List<(string Id, string Path)> crawls = [.. library.List()];
        HashSet<string> listed = [.. crawls.Select(crawl => crawl.Id)];
        lock (_sync)
        {
            // Live crawls are listed whatever the library says: one can't be removed while it runs.
            crawls.AddRange([.. _crawls.Values.Where(live => !listed.Contains(live.Id)).Select(live => (live.Id, live.Path))]);
        }

        return
        [
            .. crawls.Select(crawl => Item(crawl.Id, crawl.Path))
                .OfType<CrawlListItem>()
                .OrderByDescending(item => item.StartedAt ?? DateTime.MinValue)
                .ThenBy(item => item.Path, StringComparer.Ordinal)
        ];
    }

    public CrawlListItem Get(string id)
    {
        var path = PathOf(id);
        return Item(id, path) ?? throw EngineErrors.ForRead(summaries.Get(id, path).Error ?? new InvalidOperationException(EngineErrors.NOT_A_CRAWL));
    }

    /// <summary>The file of a crawl the library knows, live ones included.</summary>
    public string PathOf(string id)
    {
        if (!CrawlIds.IsWellFormed(id))
        {
            throw Problems.CrawlNotFound();
        }

        lock (_sync)
        {
            if (_crawls.TryGetValue(id, out var live))
            {
                return live.Path;
            }
        }

        return library.Find(id) ?? throw Problems.CrawlNotFound();
    }

    public CrawlListItem Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Problems.BadRequest("Give the full path of a .sicrawl file.");
        }

        if (!CrawlLibrary.IsCrawlFileName(path))
        {
            throw Problems.NotACrawl("Only .sicrawl files can be opened.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw Problems.FileError($"There's no crawl file at {fullPath}.");
        }

        var summary = EngineErrors.Read(() => CrawlSummaryReader.Read(fullPath));
        var id = library.Add(fullPath, summary.StartUrl);
        events.PublishLibrary();
        return Get(id);
    }

    /// <summary>Takes a crawl out of the library, not off the disk.</summary>
    public void Remove(string id)
    {
        PathOf(id);
        lock (_sync)
        {
            ThrowIfLive(id);
            library.Remove(id);
        }

        events.PublishLibrary();
    }

    public CrawlListItem Restore(string id)
    {
        PathOf(id);
        library.Restore(id);
        events.PublishLibrary();
        return Get(id);
    }

    /// <summary>
    /// Deletes the crawl file with its write-ahead log, shared memory and lock files. The log goes first: a log left
    /// beside a new file of the same name would be read into it.
    /// </summary>
    public void DeleteFile(string id)
    {
        var path = PathOf(id);
        lock (_sync)
        {
            ThrowIfLive(id);
            if (IsInUse(path))
            {
                throw Problems.CrawlBusy(IN_USE_ELSEWHERE);
            }

            try
            {
                foreach (var file in new[] { path + "-wal", path + "-shm", path, path + "-lock" })
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Problems.FileError("The crawl file couldn't be deleted. It may be open in another program.", exception.Message);
            }

            library.Forget(id);
            summaries.Invalidate(id);
            alerts.Forget(id);
        }

        _logger.LogInformation("Deleted the crawl file {Path}", path);
        events.PublishLibrary();
    }

    /// <summary>Creates <c>host-yyyyMMdd-HHmmss.sicrawl</c> in the data folder and starts crawling into it.</summary>
    public async Task<CrawlListItem> StartAsync(CrawlOptionsDto requested, bool analyzeWhenDone)
    {
        var crawlOptions = requested.ToCrawlOptions(out var mappingErrors);
        var errors = OptionFields.Merge(mappingErrors, crawlOptions.ValidateFields());
        if (errors.Count > 0)
        {
            throw Problems.InvalidOptions(errors);
        }

        try
        {
            Directory.CreateDirectory(options.DataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Problems.FileError($"The data folder {options.DataDirectory} can't be used.", exception.Message);
        }

        LiveCrawl crawl;
        lock (_sync)
        {
            ThrowIfStopping();
            var path = FreePath(CrawlDatabase.DefaultDatabasePath(crawlOptions.StartUrl));
            crawl = new LiveCrawl(CrawlIds.For(path), path, crawlOptions.StartUrl) { AnalyzeWhenDone = analyzeWhenDone };
            _crawls[crawl.Id] = crawl;
            library.Created(crawl.Id, path);
            crawl.Lifetime = Task.Run(() => RunAsync(crawl, new CrawlRequest(crawlOptions, path)));
        }

        await crawl.Started.Task.ConfigureAwait(false);
        events.PublishLibrary();
        return Get(crawl.Id);
    }

    /// <summary>
    /// Continues a stopped or interrupted crawl from its file. Only how it talks to the site may change, as with the
    /// CLI; the secrets a crawl file doesn't keep must be given again, or the UI is told which to ask for.
    /// </summary>
    public async Task<CrawlListItem> ContinueAsync(string id, ContinueCrawlRequest request)
    {
        var path = PathOf(id);
        lock (_sync)
        {
            ThrowIfStopping();
            ThrowIfLive(id);
        }

        if (!File.Exists(path))
        {
            throw Problems.FileError($"The crawl file {path} isn't there any more.");
        }

        if (IsInUse(path))
        {
            throw Problems.CrawlBusy(IN_USE_ELSEWHERE);
        }

        var headers = CrawlOptionsDto.Headers(request.ExtraHeaders, out var headerErrors);
        var plan = EngineErrors.Read(() => CrawlDatabase.ReadOptions(path)).PlanResume(new ResumeChanges
        {
            Concurrency = request.Concurrency,
            MaxRequestsPerSecond = request.MaxRequestsPerSecond,
            RequestTimeoutSeconds = request.RequestTimeoutSeconds,
            ExtraHeaders = headers,
            ProxyUrl = request.ProxyUrl
        });

        var missingHeaders = request.SkipMissingHeaders ? [] : plan.MissingHeaders;
        if (plan.MissingProxyCredentials || missingHeaders.Count > 0)
        {
            throw Problems.NeedsSecrets(missingHeaders, plan.MissingProxyCredentials);
        }

        var errors = OptionFields.Merge(headerErrors, plan.Options.ValidateFields());
        if (errors.Count > 0)
        {
            throw Problems.InvalidOptions(errors);
        }

        LiveCrawl crawl;
        lock (_sync)
        {
            ThrowIfStopping();
            ThrowIfLive(id);
            crawl = new LiveCrawl(id, path, plan.Options.StartUrl) { AnalyzeWhenDone = true };
            _crawls[id] = crawl;
            crawl.Lifetime = Task.Run(() => RunAsync(crawl, new CrawlRequest(plan.Options, path, Resume: true)));
        }

        await crawl.Started.Task.ConfigureAwait(false);
        events.PublishLibrary();
        return Get(id);
    }

    public void Pause(string id) => SetPaused(id, paused: true);

    public void Resume(string id) => SetPaused(id, paused: false);

    /// <summary>Stops a live crawl once its in-flight requests settle. Its file stays resumable.</summary>
    public void Stop(string id)
    {
        PathOf(id);
        CrawlJob? job;
        lock (_sync)
        {
            if (!_crawls.TryGetValue(id, out var crawl) || crawl.State is LiveCrawlState.Analyzing)
            {
                throw Problems.CrawlBusy("This crawl isn't running here.");
            }

            if (crawl.State is LiveCrawlState.Stopping)
            {
                return;
            }

            crawl.State = LiveCrawlState.Stopping;
            crawl.StopRequested = true;
            job = crawl.Job;
        }

        events.PublishCrawl(new CrawlEventDto(id, CrawlListStatus.Stopping), crawlEnded: false);

        // A crawl still opening its file is stopped as soon as it is open.
        _ = job?.StopAsync();
    }

    /// <summary>Starts analysing a crawl that isn't being written to. Progress arrives as <c>crawl</c> events.</summary>
    public void Analyze(string id)
    {
        var path = PathOf(id);
        lock (_sync)
        {
            ThrowIfStopping();
            if (_crawls.TryGetValue(id, out var live))
            {
                throw live.State is LiveCrawlState.Analyzing
                    ? Problems.CrawlBusy("This crawl is already being analysed.")
                    : Problems.NotAnalysable("This crawl is still running. Stop it, or let it finish, before analysing it.");
            }
        }

        if (!File.Exists(path))
        {
            throw Problems.FileError($"The crawl file {path} isn't there any more.");
        }

        // The engine checks this again once it holds the file: these say why in the UI's words, before a 202.
        var summary = EngineErrors.Read(() => CrawlSummaryReader.Read(path));
        var inUse = IsInUse(path);
        if (summary.Status is CrawlStatus.Running or CrawlStatus.Paused)
        {
            throw Problems.NotAnalysable(inUse
                ? "This crawl is still running in another program. Analyse it once it has finished."
                : "This crawl was interrupted. Continue it, and let it finish or stop it, before analysing it.");
        }

        if (inUse)
        {
            throw Problems.CrawlBusy(IN_USE_ELSEWHERE);
        }

        lock (_sync)
        {
            // Something may have started on it while its file was read.
            ThrowIfStopping();
            ThrowIfLive(id);
            var crawl = new LiveCrawl(id, path, summary.StartUrl) { State = LiveCrawlState.Analyzing, Analysis = new CancellationTokenSource() };
            _crawls[id] = crawl;
            crawl.Lifetime = Task.Run(async () =>
            {
                try
                {
                    await AnalyzeAsync(crawl).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Nothing awaits this task but the shutdown, so whatever went wrong is logged here.
                    _logger.LogError(exception, "Following the analysis of {Path} failed", path);
                }
            });
        }
    }

    /// <summary>Cancels a running analysis, which then stores nothing: the previous results stay as they were.</summary>
    public void CancelAnalysis(string id)
    {
        PathOf(id);
        CancellationTokenSource? analysis;
        lock (_sync)
        {
            analysis = _crawls.GetValueOrDefault(id) is { State: LiveCrawlState.Analyzing } crawl ? crawl.Analysis : null;
        }

        analysis?.Cancel();
    }

    public IReadOnlyList<AlertDto> Alerts(string id)
    {
        PathOf(id);
        return alerts.For(id);
    }

    /// <summary>
    /// Shutdown: every crawl stops and stays resumable, every analysis is cancelled and stores nothing, and nothing new
    /// starts. Waits for them all, as long as the host allows.
    /// </summary>
    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        List<Task> lifetimes = [];
        List<CrawlJob> jobs = [];
        List<CancellationTokenSource> analyses = [];
        lock (_sync)
        {
            _stopping = true;
            foreach (var crawl in _crawls.Values)
            {
                lifetimes.Add(crawl.Lifetime);
                switch (crawl.State)
                {
                    case LiveCrawlState.Analyzing:
                        analyses.Add(crawl.Analysis!);
                        break;
                    case LiveCrawlState.Stopping:
                        break;
                    default:
                        crawl.State = LiveCrawlState.Stopping;
                        crawl.StopRequested = true;
                        if (crawl.Job is { } job)
                        {
                            jobs.Add(job);
                        }

                        break;
                }
            }
        }

        _logger.LogInformation("Shutting down: stopping {Crawls} crawls and cancelling {Analyses} analyses", jobs.Count, analyses.Count);
        foreach (var job in jobs)
        {
            _ = job.StopAsync();
        }

        foreach (var analysis in analyses)
        {
            await analysis.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await Task.WhenAll(lifetimes).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shut down before every crawl had stopped; those files say they are running, and can be continued");
        }
    }

    internal void OnProgress(LiveCrawl crawl, CrawlProgress progress)
    {
        CrawlListStatus status;
        lock (_sync)
        {
            // Progress that arrives once the crawl has ended here (a pause racing its end) would bring it back to life
            // for streams that connect later.
            if (!_crawls.TryGetValue(crawl.Id, out var live) || !ReferenceEquals(live, crawl) || crawl.State is LiveCrawlState.Analyzing)
            {
                return;
            }

            crawl.Progress = progress;
            status = crawl.State is LiveCrawlState.Stopping && progress.Status is CrawlStatus.Running or CrawlStatus.Paused
                ? CrawlListStatus.Stopping
                : StatusOf(progress.Status);
        }

        events.PublishProgress(ProgressEventDto.From(crawl.Id, status, progress));
    }

    internal void OnNotice(LiveCrawl crawl, CrawlNotice notice)
    {
        var alert = new AlertDto(crawl.Id, notice.Level, notice.Code, notice.Message, DateTime.UtcNow);
        alerts.Add(alert);
        events.PublishAlert(alert);
    }

    /// <summary>Opens the crawl's file (off the request thread: continuing reads every URL into memory), crawls, then analyses.</summary>
    private async Task RunAsync(LiveCrawl crawl, CrawlRequest request)
    {
        CrawlJob job;
        try
        {
            job = _engine.Start(request, new SidecarCrawlObserver(this, crawl));
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _crawls.Remove(crawl.Id);
            }

            crawl.Started.SetException(StartProblem(exception, request));
            return;
        }

        try
        {
            await CrawlAsync(crawl, request, job).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Nothing awaits this task but the shutdown, so whatever went wrong is logged here, and a request still
            // waiting for the crawl to start hears of it.
            _logger.LogError(exception, "Following the crawl of {Path} failed", crawl.Path);
            crawl.Started.TrySetException(new ProblemException(Problems.Internal(exception), exception));
            lock (_sync)
            {
                _crawls.Remove(crawl.Id);
            }
        }
    }

    private async Task CrawlAsync(LiveCrawl crawl, CrawlRequest request, CrawlJob job)
    {
        bool stopNow;
        lock (_sync)
        {
            crawl.Job = job;
            stopNow = crawl.StopRequested;
            crawl.State = stopNow ? LiveCrawlState.Stopping : LiveCrawlState.Running;
        }

        _logger.LogInformation("{Action} {StartUrl} into {Path}", request.Resume ? "Continuing" : "Crawling", crawl.StartUrl, crawl.Path);
        crawl.Started.TrySetResult();
        events.PublishCrawl(new CrawlEventDto(crawl.Id, stopNow ? CrawlListStatus.Stopping : CrawlListStatus.Running), crawlEnded: false);
        OnProgress(crawl, job.Progress);
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
            _logger.LogError(exception, "The crawl of {Path} failed", crawl.Path);
            summary = new CrawlSummary(CrawlStatus.Failed, job.Progress, crawl.Path, exception.Message);
        }
        finally
        {
            await job.DisposeAsync().ConfigureAwait(false);
            summaries.Invalidate(crawl.Id);
        }

        bool analyse;
        lock (_sync)
        {
            analyse = summary.Status is CrawlStatus.Completed && crawl.AnalyzeWhenDone && !_stopping;
            crawl.Job = null;
            if (analyse)
            {
                crawl.State = LiveCrawlState.Analyzing;
                crawl.Analysis = new CancellationTokenSource();
            }
            else
            {
                _crawls.Remove(crawl.Id);
            }
        }

        _logger.LogInformation("The crawl of {Path} {Status}: {Fetched} URLs fetched", crawl.Path, summary.Status, summary.Progress.Fetched);
        events.PublishCrawl(new CrawlEventDto(crawl.Id, StatusOf(summary.Status), summary.Error), crawlEnded: true);
        events.PublishLibrary();
        if (analyse)
        {
            await AnalyzeAsync(crawl).ConfigureAwait(false);
        }
    }

    private async Task AnalyzeAsync(LiveCrawl crawl)
    {
        var cancellation = crawl.Analysis!;
        string? error = null;
        try
        {
            events.PublishCrawl(new CrawlEventDto(crawl.Id, CrawlListStatus.Analyzing), crawlEnded: false);
            var summary = await new CrawlAnalyzer(loggerFactory).AnalyzeAsync(crawl.Path, cancellationToken: cancellation.Token).ConfigureAwait(false);
            _logger.LogInformation("Analysed {Pages} pages of {Path} in {Duration}", summary.PagesAnalyzed, crawl.Path, summary.Duration);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _logger.LogInformation("The analysis of {Path} was cancelled; its previous results are unchanged", crawl.Path);
        }
        catch (Exception exception) when (EngineErrors.IsExpected(exception))
        {
            _logger.LogWarning("The analysis of {Path} failed: {Error}", crawl.Path, exception.Message);
            error = EngineErrors.Message(exception);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The analysis of {Path} failed", crawl.Path);
            error = $"The analysis failed: {exception.Message}";
        }
        finally
        {
            lock (_sync)
            {
                _crawls.Remove(crawl.Id);
            }

            summaries.Invalidate(crawl.Id);
        }

        // Back to what the file says (completed, or stopped), with the new results.
        var status = Item(crawl.Id, crawl.Path)?.Status ?? CrawlListStatus.Completed;
        events.PublishCrawl(new CrawlEventDto(crawl.Id, status, error), crawlEnded: true);
        events.PublishLibrary();
    }

    private void SetPaused(string id, bool paused)
    {
        PathOf(id);
        LiveCrawl? crawl;
        CrawlJob job;
        lock (_sync)
        {
            if (!_crawls.TryGetValue(id, out crawl) || crawl is not { State: LiveCrawlState.Running or LiveCrawlState.Paused, Job: { } live })
            {
                throw Problems.CrawlBusy("This crawl isn't running here.");
            }

            if (crawl.State == (paused ? LiveCrawlState.Paused : LiveCrawlState.Running))
            {
                return;
            }

            // Under the lock, so a pause and a resume arriving together leave the crawl as its state says.
            job = live;
            crawl.State = paused ? LiveCrawlState.Paused : LiveCrawlState.Running;
            if (paused)
            {
                job.Pause();
            }
            else
            {
                job.Resume();
            }
        }

        events.PublishCrawl(new CrawlEventDto(id, paused ? CrawlListStatus.Paused : CrawlListStatus.Running), crawlEnded: false);
        OnProgress(crawl, job.Progress);
    }

    /// <summary>
    /// The library's view of one crawl: live state from here, the rest from the file's cached summary. Null for a file
    /// that isn't a crawl this engine can read.
    /// </summary>
    private CrawlListItem? Item(string id, string path)
    {
        (LiveCrawlState State, CrawlProgress? Progress, string StartUrl)? live;
        lock (_sync)
        {
            live = _crawls.TryGetValue(id, out var crawl) ? (crawl.State, crawl.Progress, crawl.StartUrl) : null;
        }

        var cached = summaries.Get(id, path);
        var summary = cached.Summary;
        if (live is null && summary is null && cached.Exists)
        {
            return null;
        }

        var crawling = live is { State: not LiveCrawlState.Analyzing };
        var startUrl = summary?.StartUrl ?? live?.StartUrl ?? library.OpenedStartUrl(id);
        var analyzedAt = summary?.AnalyzedAt;
        return new CrawlListItem
        {
            Id = id,
            Path = path,
            FileName = Path.GetFileName(path),
            Site = SiteOf(startUrl),
            StartUrl = startUrl,
            Status = live is { } state ? StatusOf(state.State) : StoredStatus(summary, cached.Exists, path),
            StartedAt = summary?.StartedAt.UtcDateTime,
            FinishedAt = crawling ? null : summary?.FinishedAt?.UtcDateTime,
            UpdatedAt = summary?.UpdatedAt.UtcDateTime,
            UrlCount = (crawling ? live?.Progress?.Fetched : null) ?? summary?.FetchedUrlCount ?? 0,
            PageCount = (crawling ? live?.Progress?.HtmlPages : null) ?? summary?.PageCount ?? 0,
            Issues = summary?.FlaggedChecks is { } flagged
                ? new IssueCountsDto(flagged.GetValueOrDefault(IssueSeverity.Error), flagged.GetValueOrDefault(IssueSeverity.Warning),
                    flagged.GetValueOrDefault(IssueSeverity.Notice))
                : null,
            AnalyzedAt = analyzedAt?.UtcDateTime,
            OutOfDate = analyzedAt is not null && (crawling || summary!.UpdatedAt > analyzedAt),
            SizeBytes = cached.Stamp?.SizeBytes ?? 0,

            // A crawl starting here may not have created its file yet.
            Missing = live is null && !cached.Exists
        };
    }

    /// <summary>
    /// A crawl not live here, as its file says. Running is what an interrupted crawl leaves behind, so the lock says
    /// whether another process is crawling it. A missing file shows what was last known of it, or stopped.
    /// </summary>
    private static CrawlListStatus StoredStatus(CrawlFileSummary? summary, bool exists, string path)
    {
        if (summary is null)
        {
            return CrawlListStatus.Stopped;
        }

        var inUse = exists && IsInUse(path);
        return summary.Status switch
        {
            CrawlStatus.Running or CrawlStatus.Paused => inUse ? CrawlListStatus.InUse : CrawlListStatus.Interrupted,
            _ when inUse => CrawlListStatus.InUse,
            var status => StatusOf(status)
        };
    }

    private static bool IsInUse(string path)
    {
        try
        {
            return CrawlDatabase.IsInUse(path);
        }
        catch (Exception exception) when (EngineErrors.IsExpected(exception))
        {
            // A lock file this user can't open belongs to nobody who could be writing with us.
            return false;
        }
    }

    private static CrawlListStatus StatusOf(LiveCrawlState state) => state switch
    {
        LiveCrawlState.Starting or LiveCrawlState.Running => CrawlListStatus.Running,
        LiveCrawlState.Paused => CrawlListStatus.Paused,
        LiveCrawlState.Stopping => CrawlListStatus.Stopping,
        _ => CrawlListStatus.Analyzing
    };

    private static CrawlListStatus StatusOf(CrawlStatus status) => status switch
    {
        CrawlStatus.Running => CrawlListStatus.Running,
        CrawlStatus.Paused => CrawlListStatus.Paused,
        CrawlStatus.Stopped => CrawlListStatus.Stopped,
        CrawlStatus.Completed => CrawlListStatus.Completed,
        _ => CrawlListStatus.Failed
    };

    /// <summary>The host without a leading www.: example.com and www.example.com are one site.</summary>
    private static string? SiteOf(string? startUrl)
    {
        if (startUrl is null || !Uri.TryCreate(startUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>The engine's own name for the file, numbered when two crawls of a site start in the same second.</summary>
    private string FreePath(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var number = 1; ; number++)
        {
            var path = Path.Combine(options.DataDirectory, number == 1 ? fileName : $"{stem}-{number}{Path.GetExtension(fileName)}");
            if (!File.Exists(path) && !File.Exists(path + "-wal") && _crawls.Values.All(crawl => !CrawlIds.PATHS.Equals(crawl.Path, path)))
            {
                return path;
            }
        }
    }

    private void ThrowIfLive(string id)
    {
        if (_crawls.TryGetValue(id, out var crawl))
        {
            throw Problems.CrawlBusy(crawl.State is LiveCrawlState.Analyzing ? ANALYSING_HERE : RUNNING_HERE);
        }
    }

    private void ThrowIfStopping()
    {
        if (_stopping)
        {
            throw Problems.CrawlBusy(SHUTTING_DOWN);
        }
    }

    /// <summary>
    /// Why the engine couldn't open a crawl. The lock was free a moment ago, so a refusal now means another process took
    /// the file in between; for a crawl being continued, anything else means the file isn't the crawl it was.
    /// </summary>
    private ProblemException StartProblem(Exception exception, CrawlRequest request)
    {
        if (exception is ArgumentException { ParamName: "request" })
        {
            return Problems.InvalidOptions(request.Options.ValidateFields());
        }

        if (!EngineErrors.IsExpected(exception))
        {
            _logger.LogError(exception, "Could not open {Path}", request.DatabasePath);
            return new ProblemException(Problems.Internal(exception), exception);
        }

        _logger.LogWarning("Could not open {Path}: {Error}", request.DatabasePath, exception.Message);
        return exception is InvalidOperationException && (!request.Resume || IsInUse(request.DatabasePath))
            ? Problems.CrawlBusy(exception.Message)
            : EngineErrors.ForRead(exception);
    }
}
