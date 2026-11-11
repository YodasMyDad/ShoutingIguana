using System.Diagnostics;
using System.Security.Cryptography;
using ShoutingIguana.Engine.Export;
using ShoutingIguana.Engine.Export.Models;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Errors.Models;
using ShoutingIguana.Sidecar.Events;
using ShoutingIguana.Sidecar.Exports.Models;
using ShoutingIguana.Sidecar.Library;

namespace ShoutingIguana.Sidecar.Exports;

/// <summary>
/// The exports this process runs: each one a job, as an analysis is. A call starts one and is answered at once; how far
/// it has got, and how it ended, is told by <c>export</c> events and by <c>GET /api/exports/{exportId}</c>; it can be
/// cancelled, and a cancelled or failed export leaves no file. An export only reads its crawl, a page of rows at a time
/// through the grids' own readers, so it holds nothing up: a crawl goes on, and an analysis or a continue starts, while
/// one runs (the export then ends, saying the crawl changed under it).
/// <list type="bullet">
/// <item>What can be told without reading the crawl is refused before the export starts: a kind or a query that can't
/// be, a path that can't be written or whose file is open in another program.</item>
/// <item>Each export runs on a thread of its own, at most <see cref="MAX_RUNNING"/> at once.</item>
/// <item>The exports that ended are kept (the last <see cref="KEPT"/>), so a UI that missed an event can still ask.</item>
/// </list>
/// </summary>
internal sealed class ExportManager(CrawlManager crawls, ReadCache cache, EventHub events, ILogger<ExportManager> logger)
{
    /// <summary>The exports that run at once: each keeps a core and a disk busy.</summary>
    public const int MAX_RUNNING = 4;

    /// <summary>The ended exports that can still be asked for.</summary>
    private const int KEPT = 64;

    /// <summary>The least time between two events of one export's progress: four a second, as an analysis's.</summary>
    private static readonly TimeSpan PROGRESS_EVERY = TimeSpan.FromMilliseconds(250);

    private static readonly StringComparer PATHS = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, ExportJob> _jobs = new(StringComparer.Ordinal);
    private readonly Queue<string> _ended = new();
    private bool _stopping;

    /// <summary>Tests only: called on an export's thread as it starts, before it reads or writes anything.</summary>
    internal Action<ExportJob>? Starting { get; set; }

    /// <summary>Starts an export of a crawl and returns it, running.</summary>
    public ExportDto Start(string crawlId, ExportRequestDto body)
    {
        var crawlPath = crawls.PathOf(crawlId);
        var request = ExportRequests.Request(body);
        string path;
        try
        {
            CrawlExporter.Check(request);
            path = TargetPath(body.Path, request.Format, crawlPath);
            CrawlExporter.CheckWritable(path);
        }
        catch (Exception exception) when (ExportProblems.For(exception) is { } problem)
        {
            throw new ProblemException(problem, exception);
        }

        ExportJob job;
        lock (_sync)
        {
            if (_stopping)
            {
                throw Problems.ExportBusy("The engine is shutting down.");
            }

            var running = _jobs.Values.Where(other => other.Status == ExportStatus.Running).ToList();
            if (running.Any(other => PATHS.Equals(other.Path, path)))
            {
                throw Problems.ExportBusy("Another export is writing this file. Wait for it to finish, or cancel it.");
            }

            if (running.Count >= MAX_RUNNING)
            {
                throw Problems.ExportBusy($"{MAX_RUNNING} exports are running already. Wait for one to finish, or cancel one.");
            }

            job = new ExportJob(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), crawlId, request, path);
            _jobs[job.Id] = job;
            Publish(job);
            job.Lifetime = Task.Factory.StartNew(() => Run(job, crawlPath), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return Dto(job);
        }
    }

    public ExportDto Get(string exportId)
    {
        lock (_sync)
        {
            return _jobs.TryGetValue(exportId, out var job) ? Dto(job) : throw Problems.ExportNotFound();
        }
    }

    /// <summary>Cancels an export that's running; one that has ended stays as it ended.</summary>
    public void Cancel(string exportId)
    {
        CancellationTokenSource? cancel;
        lock (_sync)
        {
            cancel = _jobs.TryGetValue(exportId, out var job) ? job.Status == ExportStatus.Running ? job.Cancel : null : throw Problems.ExportNotFound();
        }

        cancel?.Cancel();
    }

    /// <summary>Shutdown: every export is cancelled, leaving no file, and nothing new starts. Waits for them, as long as the host allows.</summary>
    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        List<ExportJob> running;
        lock (_sync)
        {
            _stopping = true;
            running = [.. _jobs.Values.Where(job => job.Status == ExportStatus.Running)];
        }

        foreach (var job in running)
        {
            await job.Cancel.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await Task.WhenAll(running.Select(job => job.Lifetime)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Shut down before every export had stopped; a part-written file may be left beside its target");
        }
    }

    /// <summary>
    /// The file an export may write, from the path the UI gave: a full path on this computer. One on another computer is
    /// refused before anything touches it, as a crawl's is: writing there would have the sidecar authenticate to it.
    /// </summary>
    private static string TargetPath(string? path, ExportFormat format, string crawlPath)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw Problems.InvalidExport("path", "Give the full path of the file to save to.");
        }

        if (CrawlPaths.IsUncOrDevicePath(path) || CrawlPaths.IsNetworkPath(CrawlPaths.Resolve(path)))
        {
            throw Problems.InvalidExport("path", "A file can’t be saved straight to another computer from here. Save it on this computer, then copy it there.");
        }

        return CrawlExporter.CheckPath(path, format, crawlPath);
    }

    private void Run(ExportJob job, string crawlPath)
    {
        try
        {
            Starting?.Invoke(job);
            var settings = new ExportSettings { Cache = cache, Progress = new JobProgress(this, job), OutOfDateBy = crawls.AnalysisSettings };
            var result = CrawlExporter.Export(crawlPath, job.Request, job.Path, settings, job.Cancel.Token);
            End(job, ExportStatus.Done, result, null);
            logger.LogInformation("Exported {Kind} of {Crawl} to {Path}: {Rows} rows, {Bytes} bytes", job.Request.Kind, crawlPath, job.Path, result.Rows, result.Bytes);
        }
        catch (OperationCanceledException) when (job.Cancel.IsCancellationRequested)
        {
            End(job, ExportStatus.Cancelled, null, null);
        }
        catch (Exception exception)
        {
            var problem = ExportProblems.For(exception);
            if (problem is null)
            {
                // A bug: logged in full, and answered with its message only.
                logger.LogError(exception, "Exporting {Kind} of {Crawl} to {Path} failed", job.Request.Kind, crawlPath, job.Path);
            }
            else
            {
                logger.LogInformation("Exporting {Kind} of {Crawl} to {Path} ended: {Problem}", job.Request.Kind, crawlPath, job.Path, problem.Title);
            }

            End(job, ExportStatus.Failed, null, problem ?? Problems.Internal(exception));
        }
    }

    private void OnProgress(ExportJob job, ExportProgress progress)
    {
        lock (_sync)
        {
            (job.RowsWritten, job.RowsExpected) = (progress.RowsWritten, progress.RowsExpected);

            // The count as it's known and the first rows at once, then a few times a second: the end is told when it ends.
            if (job.ProgressSentAt == 0 || Stopwatch.GetElapsedTime(job.ProgressSentAt) >= PROGRESS_EVERY)
            {
                job.ProgressSentAt = Stopwatch.GetTimestamp();
                Publish(job);
            }
        }
    }

    private void End(ExportJob job, ExportStatus status, ExportResult? result, ProblemDto? error)
    {
        lock (_sync)
        {
            (job.Status, job.Result, job.Error, job.FinishedAt) = (status, result, error, DateTime.UtcNow);
            if (result is not null)
            {
                job.RowsWritten = result.Rows;
            }

            Publish(job);
            _ended.Enqueue(job.Id);
            while (_ended.Count > KEPT)
            {
                _jobs.Remove(_ended.Dequeue());
            }
        }
    }

    /// <summary>Under the lock, so an export's events go out in the order its states were.</summary>
    private void Publish(ExportJob job) => events.PublishExport(Dto(job), ended: job.Status != ExportStatus.Running);

    private static ExportDto Dto(ExportJob job) => new(
        job.Id,
        job.CrawlId,
        job.Request.Kind,
        job.Request.Format,
        job.Path,
        job.Status,
        job.RowsWritten,
        job.RowsExpected,
        job.StartedAt,
        job.FinishedAt,
        job.Result is { } result ? new ExportResultDto(result.Rows, result.Bytes, result.Sheets, result.UnavailableColumns, result.TruncatedCells, result.CrawlWasRunning) : null,
        job.Error);

    /// <summary>Hands one export's progress to the manager, straight from the export's thread, so its events keep their order.</summary>
    private sealed class JobProgress(ExportManager manager, ExportJob job) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => manager.OnProgress(job, value);
    }
}
