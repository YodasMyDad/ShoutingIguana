using ShoutingIguana.Engine.Crawling;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>A crawl's progress over time stays within its bounds however long the crawl runs, evenly spread, from the first point to the latest.</summary>
public sealed class DownsampledSeriesTests
{
    [Fact]
    public void KeepsEveryPointUntilItIsFull()
    {
        var series = new DownsampledSeries<int>(8);
        foreach (var point in Enumerable.Range(0, 8))
        {
            series.Add(point);
        }

        Assert.Equal(Enumerable.Range(0, 8), series.Points());
        Assert.Equal(1, series.Stride);
    }

    [Fact]
    public void HalvesWhenFullThenKeepsOneInTwoAndAlwaysTheLatest()
    {
        var series = new DownsampledSeries<int>(8);
        foreach (var point in Enumerable.Range(0, 11))
        {
            series.Add(point);
        }

        // Full at 8: every other one went, and from 8 on one in two is kept. 10 is kept; the latest is too.
        Assert.Equal(2, series.Stride);
        Assert.Equal([0, 2, 4, 6, 8, 10], series.Points());

        series.Add(11);
        Assert.Equal([0, 2, 4, 6, 8, 10, 11], series.Points());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(599)]
    [InlineData(600)]
    [InlineData(601)]
    [InlineData(10_000)]
    [InlineData(1_000_000)]
    public void StaysWithinItsBoundsEvenlySpreadFromTheFirstPointToTheLatest(int added)
    {
        var series = new DownsampledSeries<int>(600);
        foreach (var point in Enumerable.Range(0, added))
        {
            series.Add(point);
        }

        var points = series.Points();
        Assert.InRange(points.Count, Math.Min(added, 300), 601);
        Assert.Equal(0, points[0]);
        Assert.Equal(added - 1, points[^1]);
        Assert.Equal(added, series.Added);

        // Every point kept is a stride apart; only the latest, added after them, may be closer.
        var kept = points[^1] % series.Stride == 0 ? points : points.Take(points.Count - 1).ToList();
        Assert.All(kept.Zip(kept.Skip(1)), pair => Assert.Equal(series.Stride, pair.Second - pair.First));
    }

    [Fact]
    public void AnEmptySeriesHasNoPoints() => Assert.Empty(new DownsampledSeries<int>(2).Points());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void RefusesACapacityThatCantBeHalvedEvenly(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DownsampledSeries<int>(capacity));
}
