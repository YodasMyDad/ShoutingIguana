using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>
/// Every wait is checked from both sides on the fake clock: still waiting a millisecond before it should end, and
/// acquired once it has. <see cref="HostPaceTests"/> has the pace's arithmetic; these are the gate keeping to it.
/// </summary>
public sealed class HostGateTests
{
    private static readonly TimeSpan GRACE = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TICK = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task StartsOneRequestAtATimeAtOneASecond()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);

        var first = AcquiredAtOnce(gate);
        var second = Acquire(gate);
        time.Advance(TimeSpan.FromSeconds(5));
        await AssertStillWaitingAsync(second);

        // Its turn comes when the first is over, a second after the first started: here, at once.
        first.Dispose();
        (await second.WaitAsync(GRACE)).Dispose();
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// The 100k benchmark's site answers in about a millisecond: its first second goes at one request, then the slow start
    /// doubles at most every 100 ms, so it costs the benchmark a second or two.
    /// </summary>
    [Fact]
    public async Task AHealthyLocalHostIsAtFullSpeedWithinThreeSeconds()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 16, TimeSpan.Zero, time);
        var started = time.GetUtcNow();

        while (gate.PaceInterval > TimeSpan.Zero || gate.Window < 16)
        {
            await AnswerAsync(gate, time, HostSignal.Success);
        }

        // A second at one a second, then nine doublings, each at the first answer 100 ms or more after the last.
        Assert.InRange(time.GetUtcNow() - started, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.75));
        var leases = Enumerable.Range(0, 16).Select(_ => AcquiredAtOnce(gate)).ToList();
        leases.ForEach(lease => lease.Dispose());
    }

    [Fact]
    public async Task SpacesRequestsByTheInterval()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.FromSeconds(1), time);

        AcquiredAtOnce(gate).Dispose();

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CapsConcurrencyAtTheCrawlsSetting()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.Zero, time);
        await RampAsync(gate, time);

        var first = AcquiredAtOnce(gate);
        var second = AcquiredAtOnce(gate);
        var third = Acquire(gate);
        await AssertStillWaitingAsync(third);

        first.Dispose();
        (await third.WaitAsync(GRACE)).Dispose();
        second.Dispose();
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

    /// <summary>
    /// The burst that kept a real rate limit's penalty going: after the host's pause, the requests queued behind it go one
    /// at a time at half the pace that tripped it, not all at once.
    /// </summary>
    [Fact]
    public async Task AfterAPauseTheQueuedRequestsGoOneAtATimeAtHalfThePace()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.FromMilliseconds(125), time);
        await RampAsync(gate, time);
        await SendEveryAsync(gate, time, TimeSpan.FromMilliseconds(125), 32);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30));
        var queued = Enumerable.Range(0, 4).Select(_ => Acquire(gate)).ToList();

        time.Advance(TimeSpan.FromSeconds(30) - TICK);
        await AssertStillWaitingAsync(queued[0]);
        time.Advance(TICK);
        var first = await queued[0].WaitAsync(GRACE);

        // One at a time: the next waits for the first to be over, however long that takes.
        time.Advance(TimeSpan.FromSeconds(2));
        await AssertStillWaitingAsync(queued[1]);
        first.Dispose();
        gate.Report(HostSignal.Success, startedAt: first.StartedAt);
        (await queued[1].WaitAsync(GRACE)).Dispose();

        // At half the pace: 4 a second, 250 ms apart.
        await AssertWaitsAsync(time, queued[2], TimeSpan.FromMilliseconds(250));
        await AssertWaitsAsync(time, queued[3], TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task A429WithoutRetryAfterPausesTheHostForTenSeconds()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);
        await RampAsync(gate, time);

        gate.Report(HostSignal.Throttled);

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(10));
    }

    /// <summary>A healthy answer to a request sent before the host asked for a pause doesn't end it.</summary>
    [Fact]
    public async Task AnAnswerToARequestSentBeforeAPauseDoesNotEndIt()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);
        await RampAsync(gate, time);
        var outBefore = AcquiredAtOnce(gate);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(20));
        outBefore.Dispose();
        gate.Report(HostSignal.Success, startedAt: outBefore.StartedAt);

        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task HealthyAnswersRightAfterAThrottleDoNotBringBackFullSpeed()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.FromMilliseconds(125), time);
        await RampAsync(gate, time);
        await SendEveryAsync(gate, time, TimeSpan.FromMilliseconds(125), 32);
        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));

        for (var success = 0; success < 40; success++)
        {
            gate.Report(HostSignal.Success);
        }

        AcquiredAtOnce(gate).Dispose();
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, gate.Window);
    }

    [Fact]
    public async Task TheWindowShrinksWhileRequestsAreOutAndNoMoreThanItAllowsGoAtOnce()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);
        await RampAsync(gate, time);
        var out4 = Enumerable.Range(0, 4).Select(_ => AcquiredAtOnce(gate)).ToList();

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(1), out4[0].StartedAt);
        var waiting = Enumerable.Range(0, 2).Select(_ => Acquire(gate)).ToList();
        time.Advance(TimeSpan.FromMinutes(1));

        // Three of the four slots that come back are kept, so nothing goes until the last is in.
        foreach (var lease in out4.Take(3))
        {
            lease.Dispose();
        }

        await AssertStillWaitingAsync(waiting[0]);
        out4[3].Dispose();
        var next = await waiting[0].WaitAsync(GRACE);
        await AssertStillWaitingAsync(waiting[1]);
        next.Dispose();
        time.Advance(TimeSpan.FromMinutes(1));
        (await waiting[1].WaitAsync(GRACE)).Dispose();
    }

    [Fact]
    public async Task ItRecoversSlowlyWhileTheHostAnswersWell()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.FromMilliseconds(125), time);
        await RampAsync(gate, time);
        await SendEveryAsync(gate, time, TimeSpan.FromMilliseconds(125), 32);
        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMilliseconds(250), gate.PaceInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(250), gate.ReadState().SlowedTo);

        // Thirty seconds after the pause, a tenth of the pace that tripped it back, and one more request at once.
        time.Advance(TimeSpan.FromSeconds(1) + HostPace.RECOVERY_STEP);
        gate.Report(HostSignal.Success);

        Assert.Equal(1 / 4.8, gate.PaceInterval.TotalSeconds, precision: 6);
        Assert.Equal(2, gate.Window);
    }

    [Fact]
    public async Task FailuresInARowSlowTheHostWithoutPausingIt()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.FromMilliseconds(125), time);
        await RampAsync(gate, time);
        await SendEveryAsync(gate, time, TimeSpan.FromMilliseconds(125), 32);

        gate.Report(HostSignal.Failed);
        gate.Report(HostSignal.Failed);

        Assert.Equal(TimeSpan.Zero, gate.ReadState().HeldFor);
        Assert.Equal(1, gate.Window);
        Assert.Equal(TimeSpan.FromMilliseconds(250), gate.PaceInterval);
        await AssertWaitsAsync(time, Acquire(gate), TimeSpan.FromMilliseconds(250));
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
        Assert.Equal(TimeSpan.FromSeconds(5), gate.ReadState().CrawlDelay);
        Assert.Null(gate.ReadState().SlowedTo);
    }

    private static Task<HostGateLease> Acquire(HostGate gate) => gate.AcquireAsync(CancellationToken.None).AsTask();

    private static HostGateLease AcquiredAtOnce(HostGate gate)
    {
        var acquire = Acquire(gate);
        Assert.True(acquire.IsCompletedSuccessfully, "The gate should have let the request through at once.");
        return acquire.Result;
    }

    /// <summary>A request through the gate, the clock moved on in small steps until it goes, then its answer.</summary>
    private static async Task AnswerAsync(HostGate gate, FakeTimeProvider time, HostSignal signal)
    {
        var acquire = Acquire(gate);
        for (var step = 0; !acquire.IsCompleted && step < 10_000; step++)
        {
            time.Advance(TimeSpan.FromMilliseconds(5));
            await Task.WhenAny(acquire, Task.Delay(1));
        }

        var lease = await acquire.WaitAsync(GRACE);
        lease.Dispose();

        // Answered in a millisecond, as a local host does: the slow start doubles every 100 ms.
        gate.Report(signal, startedAt: lease.StartedAt, latency: TimeSpan.FromMilliseconds(1));
    }

    /// <summary>Healthy answers through the slow start, to the crawl's own settings: three seconds of them.</summary>
    private static async Task RampAsync(HostGate gate, FakeTimeProvider time)
    {
        var started = time.GetUtcNow();
        while (time.GetUtcNow() - started < TimeSpan.FromSeconds(3))
        {
            await AnswerAsync(gate, time, HostSignal.Success);
            time.Advance(TimeSpan.FromMilliseconds(10));
        }
    }

    /// <summary>Requests sent <paramref name="interval"/> apart, answered well: the pace a throttle then halves.</summary>
    private static async Task SendEveryAsync(HostGate gate, FakeTimeProvider time, TimeSpan interval, int count)
    {
        for (var request = 0; request < count; request++)
        {
            time.Advance(interval);
            var lease = await Acquire(gate).WaitAsync(GRACE);
            lease.Dispose();
            gate.Report(HostSignal.Success, startedAt: lease.StartedAt);
        }
    }

    /// <summary>
    /// Advances the clock to a millisecond short of <paramref name="wait"/> (still waiting), then to it (acquired). It is
    /// first seen waiting, so a request just handed a slot has worked out its wait before the clock moves.
    /// </summary>
    private static async Task AssertWaitsAsync(FakeTimeProvider time, Task<HostGateLease> acquire, TimeSpan wait)
    {
        await AssertStillWaitingAsync(acquire);
        time.Advance(wait - TICK);
        await AssertStillWaitingAsync(acquire);

        time.Advance(TICK);
        (await acquire.WaitAsync(GRACE)).Dispose();
    }

    private static async Task AssertStillWaitingAsync(Task<HostGateLease> acquire) =>
        await Assert.ThrowsAsync<TimeoutException>(() => acquire.WaitAsync(TimeSpan.FromMilliseconds(100)));
}
