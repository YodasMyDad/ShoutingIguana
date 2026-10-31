using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class HostGateTests
{
    private static readonly TimeSpan GRACE = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SpacesRequestsByTheInterval()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 2, TimeSpan.FromSeconds(1), time);

        (await gate.AcquireAsync(CancellationToken.None)).Dispose();
        var second = gate.AcquireAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        (await second.WaitAsync(GRACE)).Dispose();
    }

    [Fact]
    public async Task CapsConcurrency()
    {
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, new FakeTimeProvider());

        var first = await gate.AcquireAsync(CancellationToken.None);
        var second = gate.AcquireAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(GRACE)).Dispose();
    }

    [Fact]
    public async Task HonoursRetryAfter()
    {
        var time = new FakeTimeProvider();
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30));
        var next = gate.AcquireAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        Assert.False(next.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(30));
        (await next.WaitAsync(GRACE)).Dispose();
    }

    [Fact]
    public void BacksOffAndRecovers()
    {
        var gate = new HostGate(maxConcurrency: 1, TimeSpan.Zero, new FakeTimeProvider());

        gate.Report(HostSignal.Throttled);
        gate.Report(HostSignal.Throttled);
        Assert.Equal(TimeSpan.FromMilliseconds(500), gate.CurrentBackoff);

        for (var success = 0; success < 100; success++)
        {
            gate.Report(HostSignal.Success);
        }

        Assert.Equal(TimeSpan.Zero, gate.CurrentBackoff);
    }
}
