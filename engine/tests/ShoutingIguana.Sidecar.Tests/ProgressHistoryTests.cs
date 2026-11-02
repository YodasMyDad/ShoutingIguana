using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Sidecar.Crawls;

namespace ShoutingIguana.Sidecar.Tests;

/// <summary>A live crawl's history stays within its bound however long the crawl runs, from its first point to its latest.</summary>
public sealed class ProgressHistoryTests
{
    private static readonly DateTime START = new(2026, 11, 1, 10, 15, 0, DateTimeKind.Utc);

    /// <summary>Twenty-four hours of progress, twice a second, as the engine sends it.</summary>
    [Fact]
    public void ADaysProgressIsKeptToItsBoundEvenlySpread()
    {
        var history = new ProgressHistory();
        const int TICKS = 24 * 60 * 60 * 2;
        for (var tick = 0; tick < TICKS; tick++)
        {
            history.Add(START.AddMilliseconds(500.0 * tick), Progress(fetched: tick, discovered: tick * 3));
        }

        var points = history.Points();
        Assert.InRange(points.Count, ProgressHistory.CAPACITY / 2, ProgressHistory.CAPACITY + 1);
        Assert.Equal(START, points[0].At);
        Assert.Equal(0, points[0].Fetched);
        Assert.Equal(TICKS - 1, points[^1].Fetched);
        Assert.Equal((TICKS - 1) * 3, points[^1].Discovered);

        // Evenly apart in time, the latest aside.
        var spacing = points.Zip(points.Skip(1), (first, second) => second.At - first.At).SkipLast(1).Distinct().ToList();
        Assert.Single(spacing);
        Assert.True(spacing[0] > TimeSpan.FromMinutes(2), $"{spacing[0]} apart");
    }

    [Fact]
    public void EveryPointIsKeptWhileThereIsRoom()
    {
        var history = new ProgressHistory();
        foreach (var tick in Enumerable.Range(0, 10))
        {
            history.Add(START.AddSeconds(tick), Progress(fetched: tick, discovered: tick));
        }

        var points = history.Points();
        Assert.Equal(Enumerable.Range(0, 10).Select(tick => (long)tick), points.Select(point => point.Fetched));
        Assert.All(points, point => Assert.NotNull(point.ElapsedMs));
    }

    private static CrawlProgress Progress(long fetched, long discovered) =>
        new(CrawlStatus.Running, TimeSpan.FromSeconds(fetched), discovered, discovered - fetched, 0, fetched, fetched / 2, 0, 0, 0, 0, 0, 0, 0)
        {
            InternalFetched = fetched
        };
}
