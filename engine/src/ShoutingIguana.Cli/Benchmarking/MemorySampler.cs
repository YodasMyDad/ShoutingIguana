using System.Diagnostics;
using ShoutingIguana.Cli.Benchmarking.Models;

namespace ShoutingIguana.Cli.Benchmarking;

/// <summary>Samples this process's working set and managed heap alongside crawl progress.</summary>
internal sealed class MemorySampler : IAsyncDisposable
{
    private readonly List<MemorySample> _samples = [];
    private readonly Lock _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Func<long> _fetched;
    private readonly Task _loop;

    public MemorySampler(TimeSpan interval, Func<long> fetched)
    {
        _fetched = fetched;
        _loop = RunAsync(interval);
    }

    public IReadOnlyList<MemorySample> Samples
    {
        get
        {
            lock (_sync)
            {
                return [.. _samples];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        Sample();
        _stop.Dispose();
    }

    private async Task RunAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                Sample();
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException)
        {
            // Sampling stops with the benchmark.
        }
    }

    private void Sample()
    {
        var sample = new MemorySample(_clock.Elapsed.TotalSeconds, _fetched(), Environment.WorkingSet, GC.GetTotalMemory(false));
        lock (_sync)
        {
            _samples.Add(sample);
        }
    }
}
