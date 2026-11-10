using System.Collections.Concurrent;
using System.Text.Json;
using ShoutingIguana.Engine;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Library.Models;
using ShoutingIguana.Sidecar.Models;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Library;

/// <summary>
/// Each crawl file's summary, read once and kept until the file or its write-ahead log changes size or modified time,
/// so listing many crawls stays cheap. Summaries are kept in the state folder too, so a launch doesn't read every crawl
/// again. A read has a time limit: crawl files are shared, and one crafted to be slow must not hold up the list. A file
/// that can't be read is remembered as such until it changes, or, when the reason passes (a lock), for a few seconds.
/// </summary>
internal sealed class CrawlSummaryCache
{
    private const string FILE_NAME = "summaries.json";

    /// <summary>
    /// What a kept summary holds. Bump it whenever <see cref="Engine.Storage.Models.CrawlFileSummary"/> gains or changes
    /// what it carries, since builds of one engine version share the state folder: 1 added the analysis, with what it was
    /// run against, which the out-of-date rule needs (a summary kept without it would say an old analysis still stands);
    /// 2 added the URLs crawled (a summary kept without the count would say none were).
    /// </summary>
    private const int FORMAT = 2;

    private readonly ConcurrentDictionary<string, CachedSummary> _summaries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly Lock _saving = new();
    private readonly SidecarOptions _options;
    private readonly ILogger<CrawlSummaryCache> _logger;
    private readonly string _file;
    private int _changed;

    public CrawlSummaryCache(SidecarOptions options, ILogger<CrawlSummaryCache> logger)
    {
        _options = options;
        _logger = logger;
        _file = Path.Combine(options.StateDirectory, FILE_NAME);
        Load();
    }

    /// <summary>The summary with its counts, as the file is now.</summary>
    public CachedSummary Get(string id, string path) => Get(id, path, withCounts: true, frozen: false);

    /// <summary>
    /// For a crawl live here. While it crawls, its counts come from its progress, so the summary is read without them,
    /// which costs two lookups however often its file changes. While it is analysed, nothing the summary counts changes
    /// until the analysis ends (which drops the summary), so the last one read stands.
    /// </summary>
    public CachedSummary GetWhileLive(string id, string path, bool analysing) =>
        analysing ? Get(id, path, withCounts: true, frozen: true) : Get(id, path, withCounts: false, frozen: false);

    /// <summary>For changes this process made, which a modified time may be too coarse to show, and to read a file afresh.</summary>
    public void Invalidate(string id)
    {
        if (_summaries.TryRemove(id, out var removed) && removed.HasCounts)
        {
            Interlocked.Exchange(ref _changed, 1);
        }
    }

    /// <summary>Keeps the summaries read since the last save in the state folder. A failure is only logged: it is a cache.</summary>
    public void Save()
    {
        if (Interlocked.Exchange(ref _changed, 0) == 0)
        {
            return;
        }

        lock (_saving)
        {
            List<PersistedSummary> kept =
            [
                .. _summaries
                    .Where(entry => entry.Value is { Stamp: not null, Summary: not null, Error: null, HasCounts: true } && _paths.ContainsKey(entry.Key))
                    .Select(entry => new PersistedSummary(entry.Key, _paths[entry.Key], entry.Value.Stamp!, entry.Value.Summary!))
                    .OrderBy(summary => summary.Id, StringComparer.Ordinal)
            ];

            try
            {
                StateFile.Write(_file, JsonSerializer.Serialize(new SummaryCacheFile(EngineInfo.Version, FORMAT, kept), SidecarJsonContext.Default.SummaryCacheFile));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not save the crawl summaries to {SummariesFile}", _file);
            }
        }
    }

    private CachedSummary Get(string id, string path, bool withCounts, bool frozen)
    {
        // A file that has gone keeps what was last known of it, for the library to show beside "missing".
        var stamp = FileStamp.Of(path);
        if (stamp is null)
        {
            return _summaries.TryGetValue(id, out var last) ? last with { Stamp = null } : CachedSummary.Missing;
        }

        if (_summaries.TryGetValue(id, out var cached) && IsCurrent(cached, stamp, withCounts, frozen))
        {
            return cached;
        }

        var read = Read(path, stamp, withCounts);

        // A read that failed for a passing reason (another program's lock, an IO blip, a timeout) isn't remembered: the
        // next read tries again, so the crawl shows up as soon as the file can be read, without waiting for it to change.
        // Everything else is kept until the file changes: a success, or a definite verdict (not a crawl, wrong schema,
        // a damaged file) that re-reading wouldn't alter.
        if (read.Summary is not null || read.Error is { } error && !EngineErrors.IsPassing(error))
        {
            _summaries[id] = read;
            _paths[id] = path;
            if (read.HasCounts || cached?.HasCounts == true)
            {
                Interlocked.Exchange(ref _changed, 1);
            }
        }
        else if (cached is not null)
        {
            // Drop a now-stale entry so a passing failure never serves old data.
            _summaries.TryRemove(id, out _);
        }

        return read;
    }

    private static bool IsCurrent(CachedSummary cached, FileStamp stamp, bool withCounts, bool frozen) =>
        frozen ? cached.Summary is not null && cached.HasCounts || cached.Stamp == stamp
            : cached.Stamp == stamp && (cached.HasCounts || !withCounts || cached.Error is not null);

    private CachedSummary Read(string path, FileStamp stamp, bool withCounts)
    {
        using var timeLimit = new CancellationTokenSource(_options.SummaryTimeLimit);
        try
        {
            var summary = withCounts ? CrawlSummaryReader.Read(path, timeLimit.Token) : CrawlSummaryReader.ReadWithoutCounts(path, timeLimit.Token);
            return new CachedSummary(stamp, summary, null);
        }
        catch (Exception exception) when (EngineErrors.IsExpected(exception) || exception is OperationCanceledException)
        {
            if (exception is OperationCanceledException)
            {
                _logger.LogWarning("Reading the summary of {Path} took longer than {TimeLimit}; it is listed as unreadable", path, _options.SummaryTimeLimit);
            }
            else
            {
                _logger.LogDebug(exception, "Could not read the summary of {Path}", path);
            }

            return new CachedSummary(stamp, null, exception);
        }
    }

    /// <summary>The summaries a previous launch kept; those whose file has changed since are read again when listed.</summary>
    private void Load()
    {
        if (!File.Exists(_file))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize(File.ReadAllText(_file), SidecarJsonContext.Default.SummaryCacheFile);
            if (saved?.EngineVersion != EngineInfo.Version || saved.Format != FORMAT)
            {
                return;
            }

            foreach (var summary in saved.Summaries)
            {
                _summaries[summary.Id] = new CachedSummary(summary.Stamp, summary.Summary, null);
                _paths[summary.Id] = summary.Path;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not read {SummariesFile}; every crawl's summary is read again", _file);
        }
    }
}
