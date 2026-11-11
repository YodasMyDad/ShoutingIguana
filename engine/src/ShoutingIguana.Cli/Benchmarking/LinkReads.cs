using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using ShoutingIguana.Engine.Storage;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>
/// Reads a crawl's links grid all the while the crawl runs, as the Links screen does on a crawl of any size: a search,
/// which passes over every link, and a read always part-way through the file. Two readers work in relay, each holding
/// its read until the other has begun one, so there's never a moment without a read: that is what keeps SQLite from
/// starting the crawl's write-ahead log again by itself, and what the crawl's writer folds the log in for
/// (<see cref="CrawlDatabase.FoldLog"/>), holding new reads while it does: a reader then ends its read by itself. The
/// log's size is sampled as the reads go, so the benchmark can hold it to a bound, and each read is timed from when it
/// was asked to when it had its answer, a fold it waited out included.
/// </summary>
internal sealed class LinkReads : IAsyncDisposable
{
    private const int READERS = 2;

    /// <summary>The longest a reader holds its read for the other to begin one: far longer than that takes, unless new reads are held.</summary>
    private static readonly TimeSpan RELAY_HOLD = TimeSpan.FromMilliseconds(100);

    private static readonly UrlQuery QUERY = new() { Search = "/p/1", Columns = ["from.url", "to.url", "text"], Sort = [new UrlSort("id", true)], Limit = 100 };

    private readonly string _path;
    private readonly ReadCache _cache = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly Task[] _readers;
    private int _began;
    private long _reads;
    private long _slowestTicks;
    private long _peakLogBytes;

    /// <param name="path">The crawl's file, which its writer has created.</param>
    public LinkReads(string path)
    {
        _path = path;
        _readers = [.. Enumerable.Range(0, READERS).Select(_ => Task.Factory.StartNew(Read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))];
    }

    /// <summary>The reads answered so far.</summary>
    public long Reads => Interlocked.Read(ref _reads);

    /// <summary>The longest a read took to answer, from when it was asked.</summary>
    public TimeSpan Slowest => Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _slowestTicks));

    /// <summary>The largest the crawl's write-ahead log was seen.</summary>
    public long PeakLogBytes => Interlocked.Read(ref _peakLogBytes);

    /// <summary>What the reads that failed said; none should.</summary>
    public IReadOnlyCollection<string> Errors => _errors;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_readers);
        _stop.Dispose();
    }

    private void Read()
    {
        while (!_stop.IsCancellationRequested)
        {
            var asked = Stopwatch.GetTimestamp();
            try
            {
                GridReadContext.Read(_path, _cache, _stop.Token, context =>
                {
                    var mine = Interlocked.Increment(ref _began);
                    var page = LinkGridReader.Query(context, QUERY);
                    Max(ref _slowestTicks, Stopwatch.GetTimestamp() - asked);

                    // Held, with its snapshot of the file, until the other reader has begun a read of its own.
                    for (var held = Stopwatch.StartNew(); Volatile.Read(ref _began) == mine && held.Elapsed < RELAY_HOLD;)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        Max(ref _peakLogBytes, CrawlDatabase.LogBytes(_path));
                        Thread.Sleep(1);
                    }

                    return page;
                });
                Interlocked.Increment(ref _reads);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // The benchmark's crawl is over.
            }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException or IOException or UrlQueryException)
            {
                _errors.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                if (_errors.Count >= 20)
                {
                    return;
                }
            }
        }
    }

    private static void Max(ref long target, long value)
    {
        for (var seen = Interlocked.Read(ref target); value > seen; seen = Interlocked.Read(ref target))
        {
            Interlocked.CompareExchange(ref target, value, seen);
        }
    }
}
