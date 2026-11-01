using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ShoutingIguana.Sidecar.Hosting;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>The stdin lifeline: a shutdown line, or the end of stdin, shuts the sidecar down; anything else is ignored.</summary>
public sealed class StdinLifelineTests
{
    [Fact]
    public void AShutdownLineShutsDownAndStopsReading()
    {
        var lifetime = new Lifetime();
        using var input = new StringReader("hello\n  shutdown \nafter\n");

        new StdinLifeline(input, lifetime, NullLogger<StdinLifeline>.Instance).Watch();

        Assert.Equal(1, lifetime.Stops);
        Assert.Equal("after\n", input.ReadToEnd());
    }

    [Fact]
    public void TheEndOfStdinShutsDown()
    {
        var lifetime = new Lifetime();

        new StdinLifeline(new StringReader("Shutdown\nshutdown now\n"), lifetime, NullLogger<StdinLifeline>.Instance).Watch();

        Assert.Equal(1, lifetime.Stops);
    }

    [Fact]
    public async Task ItWatchesOnlyOnceTheSidecarIsServing()
    {
        var lifetime = new Lifetime();
        var lifeline = new StdinLifeline(new StringReader(string.Empty), lifetime, NullLogger<StdinLifeline>.Instance);

        await lifeline.StartAsync(CancellationToken.None);
        Assert.Equal(0, lifetime.Stops);

        lifetime.Start();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (lifetime.Stops == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, lifetime.Stops);
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private int _stops;

        public int Stops => Volatile.Read(ref _stops);

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => _started.Cancel();

        public void StopApplication() => Interlocked.Increment(ref _stops);
    }
}
