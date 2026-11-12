using System.Diagnostics;
using Microsoft.Data.Sqlite;
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
using ShoutingIguana.Sidecar.Options;

namespace ShoutingIguana.Sidecar.Crawls;

/// <summary>
/// The crawls and analyses this process runs, and the library's view of every crawl. A crawl file has one writer at a
/// time: a crawl here, an analysis here, or something in another process (its lock says so). Every change of status
/// is published as a <c>crawl</c> event, and every change to what the library lists as a <c>library</c> event. A crawl's
/// events are published under the same lock as the change they announce, so streams hear of the changes in the order
/// they happened.
/// </summary>
internal sealed class CrawlManager(
    SidecarOptions options,
    CrawlLibrary library,
    CrawlSummaryCache summaries,
    EventHub events,
    AlertLog alerts,
    AnalysisSettingsStore analysisSettings,
    ILoggerFactory loggerFactory)
{
    private const string RUNNING_HERE = "This crawl is running. Stop it first.";
    private const string ANALYSING_HERE = "This crawl is being analysed. Wait for that to finish, or cancel it.";
    private const string IN_USE_ELSEWHERE = "Another program is writing to this crawl. Try again once it has finished.";
    private const string SHUTTING_DOWN = "The engine is shutting down.";
    private const string NETWORK_PATH = "Crawls on another computer can't be opened. Copy the crawl to this computer first.";

    private readonly Lock _sync = new();
    private readonly Dictionary<string, LiveCrawl> _crawls = new(StringComparer.Ordinal);
    private readonly CrawlEngine _engine = new(loggerFactory);
    private readonly HistoryCache _histories = new();
    private readonly ILogger<CrawlManager> _logger = loggerFactory.CreateLogger<CrawlManager>();
    private bool _stopping;

    /// <summary>Tests only: sees each crawl and progress event just before it is published, under the manager's lock.</summary>
    internal Action<object>? Publishing { get; set; }

    /// <summary>Tests only: starts a crawl in place of the engine.</summary>
    internal Func<CrawlRequest, ICrawlObserver, CrawlJob>? StartJob { get; set; }

    /// <summary>Tests only: called with the crawl's id as its analysis is about to run, the limits it runs with already taken.</summary>
    internal Action<string>? AnalysisStarting { get; set; }

    /// <summary>Tests only: called with the crawl's id once <see cref="Alerts"/> has read the file, before it adds the unstored ones.</summary>
    internal Action<string>? AlertsFileRead { get; set; }

    /// <summary>
    /// The thresholds every analysis here runs with, and so what an analysis must have used to be up to date: the engine's,
    /// with what Settings has changed over them (<see cref="AnalysisSettingsStore"/>). Each read gives the set in force
    /// then, whole and unchanging: an analysis takes it once, as it starts.
    /// </summary>
    public AnalysisSettings AnalysisSettings => analysisSettings.Current;

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

        List<CrawlListItem> items =
        [
            .. crawls.Select(crawl => Item(crawl.Id, crawl.Path))
                .OfType<CrawlListItem>()
                .OrderByDescending(item => item.StartedAt ?? DateTime.MinValue)
                .ThenBy(item => item.Path, StringComparer.Ordinal)
        ];
        summaries.Save();
        return items;
    }

    public CrawlListItem Get(string id)
    {
        var path = PathOf(id);
        var item = Item(id, path) ?? throw EngineErrors.ForRead(summaries.Get(id, path).Error ?? new InvalidOperationException(EngineErrors.NOT_A_CRAWL));
        summaries.Save();
        return item;
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

    /// <summary>
    /// Adds a crawl file from anywhere on this computer, once it is checked to be a crawl. It is read afresh, so a file
    /// that couldn't be read before (another program had it locked) is tried again.
    /// </summary>
    public CrawlListItem Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Problems.BadRequest("Give the full path of a .sicrawl file.");
        }

        // Before anything touches the path: looking at a file on another computer authenticates to it.
        if (CrawlPaths.IsUncOrDevicePath(path))
        {
            throw Problems.FileError(NETWORK_PATH);
        }

        if (!CrawlLibrary.IsCrawlFileName(path))
        {
            throw Problems.NotACrawl("Only .sicrawl files can be opened.");
        }

        var fullPath = Path.GetFullPath(path);
        if (CrawlPaths.IsNetworkPath(CrawlPaths.Resolve(fullPath)))
        {
            throw Problems.FileError(NETWORK_PATH);
        }

        if (!File.Exists(fullPath))
        {
            throw Problems.FileError($"There's no crawl file at {fullPath}.");
        }

        var id = library.IdOf(fullPath);
        summaries.Invalidate(id);
        _histories.Invalidate(id);
        var cached = summaries.Get(id, fullPath);
        if (cached.Summary is not { } summary)
        {
            throw EngineErrors.ForRead(cached.Error ?? new InvalidOperationException(EngineErrors.NOT_A_CRAWL));
        }

        library.Add(fullPath, summary.StartUrl);
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
            events.PublishLibrary();
        }
    }

    public CrawlListItem Restore(string id)
    {
        PathOf(id);
        library.Restore(id);
        events.PublishLibrary();
        return Get(id);
    }

    /// <summary>
    /// Deletes the crawl file with what SQLite keeps beside it, through the engine, which holds the crawl's lock while it
    /// does: nothing can start writing to it meanwhile, here or elsewhere.
    /// </summary>
    public void DeleteFile(string id)
    {
        var path = PathOf(id);
        lock (_sync)
        {
            ThrowIfLive(id);
            try
            {
                CrawlDatabase.Delete(path);
            }
            catch (CrawlFileInUseException)
            {
                throw Problems.CrawlBusy(IN_USE_ELSEWHERE);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Problems.FileError("The crawl file couldn't be deleted. It may be open in another program.", exception.Message);
            }

            _logger.LogInformation("Deleted the crawl file {Path}", path);
            try
            {
                library.Forget(id);
            }
            finally
            {
                // The file has gone whether or not the library could be saved.
                summaries.Invalidate(id);
                _histories.Invalidate(id);
                alerts.Forget(id);
                events.PublishLibrary();
            }
        }
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
            crawl = new LiveCrawl(library.IdOf(path), path, crawlOptions.StartUrl) { AnalyzeWhenDone = analyzeWhenDone, RunStartedAt = DateTime.UtcNow };
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
            crawl = new LiveCrawl(id, path, plan.Options.StartUrl) { AnalyzeWhenDone = true, RunStartedAt = DateTime.UtcNow, Resumed = true };
            _crawls[id] = crawl;
            crawl.Lifetime = Task.Run(() => RunAsync(crawl, new CrawlRequest(plan.Options, path, Resume: true)));
        }

        await crawl.Started.Task.ConfigureAwait(false);
        events.PublishLibrary();
        return Get(id);
    }

    public void Pause(string id) => SetPaused(id, paused: true);

    public void Resume(string id) => SetPaused(id, paused: false);

    /// <summary>Stops a live crawl: its requests are cancelled and its file closed. The file stays resumable.</summary>
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
            PublishCrawl(new CrawlEventDto(id, CrawlListStatus.Stopping), crawlEnded: false);
        }

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

        // The engine checks this again once it holds the file: these say why in the UI's words, before a 202. Only the
        // status and start URL are needed, which take two lookups however big the crawl.
        var summary = EngineErrors.Read(() => CrawlSummaryReader.ReadWithoutCounts(path));
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
            PublishCrawl(new CrawlEventDto(id, CrawlListStatus.Analyzing), crawlEnded: false);
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

    /// <summary>
    /// The crawl's alerts as its file keeps them, every run's, whoever ran it, oldest first. A crawl live here adds the ones
    /// the engine hasn't stored yet, which it does within a second or so. Those are taken before the file is read: a crawl
    /// that closes in between stores every alert it then forgets, so each is in one or the other, and one raised after
    /// they were taken is in neither, with its live event still on its way.
    /// </summary>
    public IReadOnlyList<AlertDto> Alerts(string id)
    {
        var path = PathOf(id);
        IReadOnlyList<AlertDto>? unstored = null;
        lock (_sync)
        {
            if (_crawls.ContainsKey(id))
            {
                unstored = alerts.For(id);
            }
        }

        List<AlertDto> stored = File.Exists(path) || unstored is null
            ? [.. EngineErrors.Read(() => CrawlNoticeReader.Read(path, AlertLog.DEFAULT_LIMIT)).Select(notice => AlertDto.From(id, notice))]
            : [];
        AlertsFileRead?.Invoke(id);
        if (unstored is null)
        {
            return stored;
        }

        // An alert is its id and the time it was raised: ids continue from the file's highest, which a crash can leave
        // short of the last ones a UI heard.
        HashSet<(long Id, DateTime At)> known = [.. stored.Select(alert => (alert.Id, alert.At))];
        stored.AddRange(unstored.Where(alert => !known.Contains((alert.Id, alert.At))));
        return [.. stored.OrderBy(alert => alert.Id).TakeLast(AlertLog.DEFAULT_LIMIT)];
    }

    /// <summary>
    /// The crawl's progress since it started, for its chart. A crawl live here has this run's points as the sidecar took
    /// them, after the earlier runs counted back from the file, which are read once; any other crawl has the file's alone,
    /// kept until the file changes.
    /// </summary>
    public HistoryDto History(string id)
    {
        var path = PathOf(id);
        LiveCrawl? crawl;
        lock (_sync)
        {
            crawl = _crawls.GetValueOrDefault(id);
        }

        if (crawl?.RunStartedAt is not { } runStartedAt)
        {
            var history = _histories.Get(id, path, () => EngineErrors.Read(() => CrawlHistoryReader.Read(path)));
            return new HistoryDto(id, history.StartedAt.UtcDateTime, null, [.. history.Points.Select(HistoryPointDto.FromFile)]).With(history.Runs);
        }

        // When the crawl was crawling is read from its file each time: a pause a moment ago is in it, and it's a few rows.
        var runs = EngineErrors.Read(() => CrawlRunReader.Read(path));
        var live = crawl.History.Points();
        if (!crawl.Resumed)
        {
            return new HistoryDto(id, runStartedAt, runStartedAt, live).With(runs);
        }

        // Read outside the lock, as a big crawl takes a moment, and kept. Two requests at once may both read it: either will do.
        CrawlHistory? earlier;
        lock (_sync)
        {
            earlier = crawl.EarlierRuns;
        }

        if (earlier is null)
        {
            earlier = EngineErrors.Read(() => CrawlHistoryReader.Read(path, runStartedAt));
            lock (_sync)
            {
                crawl.EarlierRuns = earlier;
            }
        }

        return new HistoryDto(id, earlier.StartedAt.UtcDateTime, runStartedAt, [.. earlier.Points.Select(HistoryPointDto.FromFile), .. live]).With(runs);
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
        lock (_sync)
        {
            PublishProgress(crawl, progress);
        }
    }

    internal void OnNotice(LiveCrawl crawl, CrawlNotice notice)
    {
        // With the id and time the crawl file keeps it under, so the UI knows it again when it reads the file's.
        var alert = AlertDto.From(crawl.Id, notice);
        alerts.Add(alert);
        events.PublishAlert(alert);
    }

    /// <summary>
    /// An analysis's progress, as it reports it. Dropped once the analysis is no longer live here: a stream that connects
    /// later must not hear of an analysis that has ended.
    /// </summary>
    internal void OnAnalysisProgress(LiveCrawl crawl, AnalysisProgress progress)
    {
        lock (_sync)
        {
            if (_crawls.TryGetValue(crawl.Id, out var live) && ReferenceEquals(live, crawl) && crawl.State is LiveCrawlState.Analyzing)
            {
                events.PublishAnalysis(new AnalysisEventDto(crawl.Id, progress.Phase, progress.Done, progress.Total));
            }
        }
    }

    /// <summary>Opens the crawl's file (off the request thread: continuing reads every URL into memory), crawls, then analyses.</summary>
    private async Task RunAsync(LiveCrawl crawl, CrawlRequest request)
    {
        // The file has every alert of the runs before this one.
        alerts.Forget(crawl.Id);
        CrawlJob job;
        try
        {
            job = (StartJob ?? _engine.Start)(request, new SidecarCrawlObserver(this, crawl));
        }
        catch (Exception exception)
        {
            // A stop may have been announced while the file was opening: whoever heard it hears how it ended too.
            var problem = StartProblem(exception, request);
            End(crawl, problem.Problem.Title);
            crawl.Started.SetException(problem);
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
            End(crawl, $"The crawl failed: {exception.Message}");
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
            crawl.Started.TrySetResult();
            PublishCrawl(new CrawlEventDto(crawl.Id, stopNow ? CrawlListStatus.Stopping : CrawlListStatus.Running), crawlEnded: false);
            PublishProgress(crawl, job.Progress);
        }

        _logger.LogInformation("{Action} {StartUrl} into {Path}", request.Resume ? "Continuing" : "Crawling", crawl.StartUrl, crawl.Path);
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
            _histories.Invalidate(crawl.Id);

            // The file has every alert now, the last ones stored as it closed.
            alerts.Forget(crawl.Id);
        }

        bool analyse;
        lock (_sync)
        {
            analyse = summary.Status is CrawlStatus.Completed && crawl.AnalyzeWhenDone && !_stopping;
            crawl.Job = null;
            PublishCrawl(new CrawlEventDto(crawl.Id, StatusOf(summary.Status), summary.Error), crawlEnded: true);
            events.PublishLibrary();
            if (analyse)
            {
                crawl.State = LiveCrawlState.Analyzing;
                crawl.Analysis = new CancellationTokenSource();
                PublishCrawl(new CrawlEventDto(crawl.Id, CrawlListStatus.Analyzing), crawlEnded: false);
            }
            else
            {
                _crawls.Remove(crawl.Id);
            }
        }

        _logger.LogInformation("The crawl of {Path} {Status}: {Fetched} URLs fetched", crawl.Path, summary.Status, summary.Progress.Fetched);
        if (analyse)
        {
            await AnalyzeAsync(crawl).ConfigureAwait(false);
        }
    }

    private async Task AnalyzeAsync(LiveCrawl crawl)
    {
        var cancellation = crawl.Analysis!;
        string? error = null;
        string? errorType = null;
        try
        {
            // The limits in force as it starts are the ones it runs with to its end, and stores: saved again meanwhile, the
            // analysis is out of date the moment it's done, and says so.
            var settings = AnalysisSettings;
            AnalysisStarting?.Invoke(crawl.Id);
            var summary = await new CrawlAnalyzer(loggerFactory)
                .AnalyzeAsync(crawl.Path, settings, new SidecarAnalysisProgress(this, crawl), cancellation.Token).ConfigureAwait(false);
            _logger.LogInformation("Analysed {Pages} pages of {Path} in {Duration}", summary.PagesAnalyzed, crawl.Path, summary.Duration);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _logger.LogInformation("The analysis of {Path} was cancelled; its previous results are unchanged", crawl.Path);
        }
        catch (CrawlFileInUseException exception)
        {
            // The file was busy for a moment (someone part-way through reading it, another program writing): the UI can
            // say so and offer to try again, as it would for the problem.
            _logger.LogWarning("The analysis of {Path} found the file busy: {Error}", crawl.Path, exception.Message);
            var busy = Problems.CrawlBusy(exception.Message).Problem;
            (error, errorType) = (busy.Title, busy.Type);
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

        // Back to what the file says (completed, or stopped), with the new results.
        End(crawl, error, errorType);
    }

    /// <summary>
    /// The crawl is no longer live here: announced with what its file says now, then the library changes. The file is
    /// read first, outside the lock; the crawl leaves the live ones and its events go out together, under it.
    /// </summary>
    private void End(LiveCrawl crawl, string? error, string? errorType = null)
    {
        summaries.Invalidate(crawl.Id);
        _histories.Invalidate(crawl.Id);
        var status = FileStatus(crawl.Id, crawl.Path);
        lock (_sync)
        {
            if (_crawls.TryGetValue(crawl.Id, out var live) && ReferenceEquals(live, crawl))
            {
                _crawls.Remove(crawl.Id);
            }

            PublishCrawl(new CrawlEventDto(crawl.Id, status, error, errorType), crawlEnded: true);
            events.PublishLibrary();
        }
    }

    private void SetPaused(string id, bool paused)
    {
        PathOf(id);
        lock (_sync)
        {
            if (!_crawls.TryGetValue(id, out var crawl) || crawl is not { State: LiveCrawlState.Running or LiveCrawlState.Paused, Job: { } job })
            {
                throw Problems.CrawlBusy("This crawl isn't running here.");
            }

            if (crawl.State == (paused ? LiveCrawlState.Paused : LiveCrawlState.Running))
            {
                return;
            }

            // Under the lock, so a pause and a resume arriving together leave the crawl as its state says, and their
            // events go out in that order.
            crawl.State = paused ? LiveCrawlState.Paused : LiveCrawlState.Running;
            if (paused)
            {
                job.Pause();
            }
            else
            {
                job.Resume();
            }

            PublishCrawl(new CrawlEventDto(id, paused ? CrawlListStatus.Paused : CrawlListStatus.Running), crawlEnded: false);
            PublishProgress(crawl, job.Progress);
        }
    }

    /// <summary>Publishes a crawl event. Called under the lock, with the change it announces.</summary>
    private void PublishCrawl(CrawlEventDto crawl, bool crawlEnded)
    {
        Debug.Assert(_sync.IsHeldByCurrentThread);
        Publishing?.Invoke(crawl);
        events.PublishCrawl(crawl, crawlEnded);
    }

    /// <summary>
    /// Publishes a live crawl's progress. Called under the lock: progress that arrives once the crawl has ended here (a
    /// pause racing its end) would bring it back to life for streams that connect later, so it is dropped.
    /// </summary>
    private void PublishProgress(LiveCrawl crawl, CrawlProgress progress)
    {
        Debug.Assert(_sync.IsHeldByCurrentThread);
        if (!_crawls.TryGetValue(crawl.Id, out var live) || !ReferenceEquals(live, crawl) || crawl.State is LiveCrawlState.Analyzing)
        {
            return;
        }

        crawl.Progress = progress;
        var status = crawl.State is LiveCrawlState.Stopping && progress.Status is CrawlStatus.Running or CrawlStatus.Paused
            ? CrawlListStatus.Stopping
            : StatusOf(progress.Status);
        var at = DateTime.UtcNow;
        crawl.History.Add(at, progress);
        var dto = ProgressEventDto.From(crawl.Id, status, progress, at);
        Publishing?.Invoke(dto);
        events.PublishProgress(dto);
    }

    /// <summary>
    /// The library's view of one crawl: live state from here, the rest from the file's cached summary. Null for a file
    /// that isn't a crawl this engine reads; a file that may be one but couldn't be read is listed as unreadable.
    /// </summary>
    private CrawlListItem? Item(string id, string path)
    {
        (LiveCrawlState State, CrawlProgress? Progress, string StartUrl)? live;
        lock (_sync)
        {
            live = _crawls.TryGetValue(id, out var crawl) ? (crawl.State, crawl.Progress, crawl.StartUrl) : null;
        }

        var cached = live is { } state
            ? summaries.GetWhileLive(id, path, analysing: state.State is LiveCrawlState.Analyzing)
            : summaries.Get(id, path);
        if (live is null && cached.IsNotACrawl)
        {
            return null;
        }

        var summary = cached.Summary;
        var unreadable = live is null && cached.IsUnreadable;
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
            Status = live is { } now ? StatusOf(now.State) : unreadable ? CrawlListStatus.Unreadable : StoredStatus(summary, cached.Exists, path),
            Error = unreadable ? EngineErrors.UnreadableMessage(cached.Error!) : null,
            StartedAt = summary?.StartedAt.UtcDateTime,
            FinishedAt = crawling ? null : summary?.FinishedAt?.UtcDateTime,
            UpdatedAt = summary?.UpdatedAt.UtcDateTime,
            CrawlingMs = (long?)(crawling && live?.Progress is { } progress ? progress.TimeCrawling : summary?.TimeCrawling)?.TotalMilliseconds,
            FetchedUrlCount = (crawling ? live?.Progress?.Fetched : null) ?? summary?.FetchedUrlCount ?? 0,
            CrawledUrlCount = (crawling ? live?.Progress?.Crawled : null) ?? summary?.CrawledUrlCount ?? 0,
            PageCount = (crawling ? live?.Progress?.HtmlPages : null) ?? summary?.PageCount ?? 0,
            Issues = summary?.FlaggedChecks is { } flagged
                ? new IssueCountsDto(flagged.GetValueOrDefault(IssueSeverity.Error), flagged.GetValueOrDefault(IssueSeverity.Warning),
                    flagged.GetValueOrDefault(IssueSeverity.Notice))
                : null,
            AnalyzedAt = analyzedAt?.UtcDateTime,
            Score = summary?.Analysis?.Score,

            // A crawl running here changes its file as it goes; otherwise the file says whether its analysis still stands.
            OutOfDate = analyzedAt is not null && (crawling || summary!.OutOfDate(AnalysisSettings).Count > 0),
            SizeBytes = cached.Stamp?.SizeBytes ?? 0,

            // A crawl starting here may not have created its file yet.
            Missing = live is null && !cached.Exists
        };
    }

    /// <summary>What a crawl's file says it is, whatever this process is doing with it.</summary>
    private CrawlListStatus FileStatus(string id, string path)
    {
        var cached = summaries.Get(id, path);
        return cached.IsUnreadable ? CrawlListStatus.Unreadable : StoredStatus(cached.Summary, cached.Exists, path);
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
            // A path the system can't use at all (too long) belongs to nobody who could be writing with us.
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
    /// Why the engine couldn't open a crawl. The engine says when another crawl or analysis holds the file (the lock was
    /// free a moment ago, so one took it in between); another program's SQLite lock is busy too. For a crawl being
    /// continued, anything else means the file isn't the crawl it was; for a new one, another program made a file of that
    /// name in the same second.
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
        return exception switch
        {
            // Someone part-way through reading the closed file, which keeps writers out: the engine's words say so.
            CrawlFileInUseException { InnerException: SqliteException } => Problems.CrawlBusy(exception.Message),
            CrawlFileInUseException => Problems.CrawlBusy(IN_USE_ELSEWHERE),
            InvalidOperationException when !request.Resume => Problems.CrawlBusy(exception.Message),
            _ => EngineErrors.ForRead(exception)
        };
    }
}
