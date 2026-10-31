using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Every wait is checked from both sides on the fake clock: still waiting a millisecond before it should end, and
/// acquired once it has.
/// </summary>
public sealed class HostGateTests
{
    private static readonly TimeSpan GRACE = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TICK = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task SpacesRequestsByTheInterval()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.FromSeconds(1), time);

        AcquiredAtOnce(gate).Dispose();

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CapsConcurrency()
    {
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, new FakeTimeProvider());

        var first = AcquiredAtOnce(gate);
        var second = Acquire(gate);
        await AssertStillWaitingAsync(second);

        first.Dispose();
        (await second.WaitAsync(GRACE)).Dispose();
    }

    [Fact]
    public async Task HonoursRetryAfter()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30));

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CapsARetryAfterPauseAtTwoMinutes()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled, TimeSpan.FromHours(1));

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task ARetryAfterThatArrivesWhileARequestWaitsStillHoldsItBack()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.FromSeconds(1), time);
        AcquiredAtOnce(gate).Dispose();
        var waiting = Acquire(gate);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30));
        time.Advance(TimeSpan.FromSeconds(1));
        await AssertStillWaitingAsync(waiting);

        await AssertWaitsAsync(time, waiting, TimeSpan.FromSeconds(29));
    }

    [Fact]
    public async Task ThrottlingWidensTheSpacingAndSuccessNarrowsItAgain()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled);
        gate.Report(HostSignal.Throttled);
        Assert.Equal(TimeSpan.FromMilliseconds(500), gate.CurrentBackoff);
        AcquiredAtOnce(gate).Dispose();
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMilliseconds(500));

        for (var success = 0; success < 40; success++)
        {
            gate.Report(HostSignal.Success);
        }

        // The spacing is worked out at each start, so once the backoff is gone, requests go back to back at once.
        Assert.Equal(TimeSpan.Zero, gate.CurrentBackoff);
        AcquiredAtOnce(gate).Dispose();
        AcquiredAtOnce(gate).Dispose();
    }

    [Fact]
    public async Task FailuresWidenTheSpacing()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);

        gate.Report(HostSignal.Failed);
        gate.Report(HostSignal.Failed);
        AcquiredAtOnce(gate).Dispose();

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMilliseconds(125));
    }

    [Fact]
    public void ASingleFailureAmongSuccessesDoesNotSlowTheSite()
    {
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, new FakeTimeProvider());

        gate.Report(HostSignal.Success);
        gate.Report(HostSignal.Failed);
        gate.Report(HostSignal.Success);
        gate.Report(HostSignal.Failed);

        Assert.Equal(TimeSpan.Zero, gate.CurrentBackoff);
        AcquiredAtOnce(gate).Dispose();
        AcquiredAtOnce(gate).Dispose();
    }

    [Fact]
    public void ABackoffEasesWithTime()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromMilliseconds(125), gate.CurrentBackoff);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, gate.CurrentBackoff);
    }

    /// <summary>
    /// Requests queued behind a backoff start as it eases: each start is spaced by the backoff as it stands then, not as
    /// it stood when the request joined the queue. Fifteen starts 250 ms apart would take 3.75 s.
    /// </summary>
    [Fact]
    public async Task RequestsQueuedBehindABackoffStartAsItEases()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 16, TimeSpan.Zero, time);
        gate.Report(HostSignal.Throttled);
        AcquiredAtOnce(gate).Dispose();
        var queued = Enumerable.Range(0, 15).Select(_ => Acquire(gate)).ToList();
        var started = time.GetUtcNow();

        while (queued.Any(acquire => !acquire.IsCompleted) && time.GetUtcNow() - started < TimeSpan.FromSeconds(10))
        {
            time.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Delay(1);
        }

        Assert.InRange(time.GetUtcNow() - started, TimeSpan.Zero, TimeSpan.FromSeconds(2.5));
        foreach (var acquire in queued)
        {
            (await acquire).Dispose();
        }
    }

    [Fact]
    public async Task AShorterPauseDoesNotCutALongerOneShort()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, time);

        gate.Pause(TimeSpan.FromMinutes(2));
        gate.Pause(TimeSpan.FromMinutes(1));

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CrawlDelayRaisesTheIntervalButNeverLowersIt()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.FromSeconds(1), time);

        gate.RaiseMinimumInterval(TimeSpan.FromSeconds(5));
        AcquiredAtOnce(gate).Dispose();
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(5));

        gate.RaiseMinimumInterval(TimeSpan.FromMilliseconds(500));
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(5));
    }

    private static Task<HostGateLease> Acquire(HostGate gate) => gate.AcquireAsync(CancellationToken.None).AsTask();

    private static HostGateLease AcquiredAtOnce(HostGate gate)
    {
        var acquire = Acquire(gate);
        Assert.True(acquire.IsCompletedSuccessfully, "The gate should have let the request through at once.");
        return acquire.Result;
    }

    /// <summary>Advances the clock to a millisecond short of <paramref name="wait"/> (still waiting), then to it (acquired).</summary>
    private static async Task AssertWaitsAsync(FakeTimeProvider time, Task<HostGateLease> acquire, TimeSpan wait)
    {
        time.Advance(wait - TICK);
        await AssertStillWaitingAsync(acquire);

        time.Advance(TICK);
        (await acquire.WaitAsync(GRACE)).Dispose();
    }

    private static async Task AssertStillWaitingAsync(Task<HostGateLease> acquire) =>
        await Assert.ThrowsAsync<TimeoutException>(() => acquire.WaitAsync(TimeSpan.FromMilliseconds(100)));
}
