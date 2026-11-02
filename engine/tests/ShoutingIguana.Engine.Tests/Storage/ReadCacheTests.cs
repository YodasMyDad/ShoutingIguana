using ShoutingIguana.Engine.Storage;

namespace ShoutingIguana.Engine.Tests.Storage;

/// <summary>
/// The cache of what the grids counted: each thing counted once however many requests want it at once, a request
/// cancelled part-way leaving the count to one that waited, the least recently used going first, long keys kept by their
/// hash, and a running crawl's counts kept only briefly.
/// </summary>
public sealed class ReadCacheTests
{
    [Fact]
    public async Task RequestsWantingWhatsBeingCountedWaitForThatCount()
    {
        var cache = new ReadCache();
        var reads = 0;
        using var release = new ManualResetEventSlim();
        long Count()
        {
            Interlocked.Increment(ref reads);
            release.Wait();
            return 42;
        }

        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.GetOrAdd("matching|title", Count))).ToList();
        await Task.Delay(200);
        release.Set();
        Assert.All(await Task.WhenAll(requests), count => Assert.Equal(42, count));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task WhenTheRequestCountingIsCancelledOneThatWaitedCountsInstead()
    {
        var cache = new ReadCache();
        using var owner = new CancellationTokenSource();
        using var ownerStarted = new ManualResetEventSlim();
        var counting = Task.Run(() => cache.GetOrAdd<long>("matching|title", () =>
        {
            ownerStarted.Set();
            Task.Delay(Timeout.Infinite, owner.Token).Wait(CancellationToken.None);
            return 1;
        }, cancellationToken: owner.Token));
        Assert.True(ownerStarted.Wait(TimeSpan.FromSeconds(10)));

        var waiting = Task.Run(() => cache.GetOrAdd<long>("matching|title", () => 2));
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);
        owner.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => counting);
        Assert.Equal(2, await waiting.WaitAsync(TimeSpan.FromSeconds(10)));

        // A request cancelled while it waits stops waiting at once.
        using var gone = new CancellationTokenSource();
        using var block = new ManualResetEventSlim();
        _ = Task.Run(() => cache.GetOrAdd<long>("facets", () =>
        {
            block.Wait();
            return 3;
        }));
        await Task.Delay(100);
        var given = Task.Run(() => cache.GetOrAdd<long>("facets", () => 4, cancellationToken: gone.Token));
        gone.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => given.WaitAsync(TimeSpan.FromSeconds(10)));
        block.Set();
    }

    [Fact]
    public void PastItsCapacityTheLeastRecentlyUsedGo()
    {
        var cache = new ReadCache(capacity: 8);
        for (var key = 0; key < 8; key++)
        {
            cache.GetOrAdd($"k{key}", () => key);
        }

        // Kept in use, the first stays; the next ones go.
        cache.GetOrAdd("k0", () => -1);
        cache.GetOrAdd("k8", () => 8);
        Assert.Equal(0, cache.GetOrAdd("k0", () => -1));
        Assert.Equal(8, cache.GetOrAdd("k8", () => -1));
        Assert.Equal(-1, cache.GetOrAdd("k1", () => -1));
        Assert.InRange(cache.Count, 1, 8);
    }

    [Fact]
    public void LongKeysAreKeptByTheirHash()
    {
        var cache = new ReadCache();
        var one = "matching|" + new string('a', 90_000);
        var other = "matching|" + new string('a', 89_999) + "b";
        Assert.Equal(1, cache.GetOrAdd(one, () => 1));
        Assert.Equal(2, cache.GetOrAdd(other, () => 2));
        Assert.Equal(1, cache.GetOrAdd(one, () => -1));
        Assert.Equal(2, cache.Count);

        // A key holds a query's conditions with their values: kept whole, 4,096 of them could take 380 MB.
        Assert.InRange(cache.KeyLength, 1, 512);
    }

    [Fact]
    public void AValueOlderThanItsAgeIsCountedAgain()
    {
        var cache = new ReadCache();
        var age = TimeSpan.FromMilliseconds(100);
        Assert.Equal(1, cache.GetOrAdd("live", () => 1, age));
        Assert.Equal(1, cache.GetOrAdd("live", () => 2, age));
        Thread.Sleep(150);
        Assert.Equal(3, cache.GetOrAdd("live", () => 3, age));
        Assert.Equal(3, cache.GetOrAdd("live", () => 4));
    }
}
