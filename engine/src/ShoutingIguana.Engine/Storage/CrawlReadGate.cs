using System.Collections.Concurrent;
using System.Diagnostics;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The grid reads of each crawl file in this process, and the writers that need the file to themselves for a moment. A
/// closed crawl is a single file in rollback-journal mode, and switching it to WAL mode (as an analysis or a continue
/// opens it), or back (as one closes it), needs no reader part-way through it: a read holds the file's shared lock until
/// it ends, and the writer would wait out its busy timeout, then give up. So a writer about to switch stops new reads of
/// the file, stops those in flight (they read again once it's done), and waits for them to let go, which takes
/// milliseconds; in WAL mode, reads and the writer don't wait for each other. Other programs reading the file aren't
/// seen here: they can still keep a writer out.
/// </summary>
internal static class CrawlReadGate
{
    /// <summary>How long a writer waits for reads to let go before it switches anyway, and SQLite's busy timeout decides.</summary>
    private static readonly TimeSpan PATIENCE = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentDictionary<string, FileGate> FILES =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// A grid read of the file begins: once no writer is switching it. The lease's token is cancelled when a writer wants
    /// the file, and disposing it ends the read, after its connection has closed.
    /// </summary>
    /// <exception cref="OperationCanceledException">The read was cancelled while it waited.</exception>
    public static ReadLease Enter(string path, CancellationToken cancellationToken)
    {
        var gate = FILES.GetOrAdd(Key(path), _ => new FileGate());
        return new ReadLease(gate, gate.Enter(cancellationToken));
    }

    /// <summary>A writer has the file to itself until this is disposed: no grid read here is part-way through it.</summary>
    public static IDisposable Exclusive(string path)
    {
        var gate = FILES.GetOrAdd(Key(path), _ => new FileGate());
        gate.BeginWrite(PATIENCE);
        return new WriteLease(gate);
    }

    private static string Key(string path) => Path.GetFullPath(path);

    /// <summary>One read of a file, which a writer can stop.</summary>
    internal sealed class ReadLease(FileGate gate, CancellationTokenSource preempted) : IDisposable
    {
        /// <summary>Cancelled when a writer wants the file: the read stops, and reads again once the writer has switched it.</summary>
        public CancellationToken Preempted => preempted.Token;

        public bool WasPreempted => preempted.IsCancellationRequested;

        public void Dispose() => gate.Exit(preempted);
    }

    private sealed class WriteLease(FileGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.EndWrite();
            }
        }
    }

    /// <summary>One file's reads in flight, and whether a writer is switching it.</summary>
    internal sealed class FileGate
    {
        private readonly object _sync = new();
        private readonly HashSet<CancellationTokenSource> _reads = [];
        private int _writers;

        public CancellationTokenSource Enter(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                while (_writers > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Monitor.Wait(_sync, TimeSpan.FromMilliseconds(50));
                }

                cancellationToken.ThrowIfCancellationRequested();
                var preempted = new CancellationTokenSource();
                _reads.Add(preempted);
                return preempted;
            }
        }

        public void Exit(CancellationTokenSource preempted)
        {
            lock (_sync)
            {
                _reads.Remove(preempted);
                Monitor.PulseAll(_sync);
            }

            preempted.Dispose();
        }

        public void BeginWrite(TimeSpan patience)
        {
            var started = Stopwatch.GetTimestamp();
            lock (_sync)
            {
                _writers++;

                // Under the lock, so no read can end (and dispose its source) meanwhile. Cancelling interrupts each read's
                // statement, which is all its callbacks do.
                foreach (var read in _reads)
                {
                    read.Cancel();
                }

                while (_reads.Count > 0 && Stopwatch.GetElapsedTime(started) < patience)
                {
                    Monitor.Wait(_sync, TimeSpan.FromMilliseconds(10));
                }
            }
        }

        public void EndWrite()
        {
            lock (_sync)
            {
                _writers--;
                Monitor.PulseAll(_sync);
            }
        }
    }
}
