using Microsoft.Extensions.Time.Testing;
using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>A section a host limits apart from the rest: its own pace, and which URLs are in it.</summary>
public sealed class SectionPaceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirst429SeedsTheIntervalFromItsRetryAfter()
    {
        var section = new SectionPace("products");

        section.Throttled(TimeSpan.FromSeconds(60), T0, T0);

        Assert.Equal(TimeSpan.FromSeconds(60), section.Interval);
        Assert.False(section.TryStart(T0 + TimeSpan.FromSeconds(59)));
        Assert.True(section.TryStart(T0 + TimeSpan.FromSeconds(60)));
        Assert.False(section.TryStart(T0 + TimeSpan.FromSeconds(61)));
        Assert.Equal("/products/", section.Path);
    }

    /// <summary>One product page got through a minute after the pause: about one a minute, a little sooner each time.</summary>
    [Fact]
    public void ItLearnsFromWhatGetsThroughAndFromWhatDoesnt()
    {
        var section = new SectionPace("products");
        section.Throttled(TimeSpan.FromSeconds(60), T0, T0);

        section.GotThrough();
        Assert.Equal(TimeSpan.FromSeconds(54), section.Interval);

        // A 429 after that doubles what it learned; the next page still waits at least the Retry-After.
        var later = T0 + TimeSpan.FromMinutes(2);
        Assert.True(section.TryStart(later));
        section.Throttled(TimeSpan.FromSeconds(60), later, later);
        Assert.Equal(TimeSpan.FromSeconds(108), section.Interval);
        Assert.Equal(later + TimeSpan.FromSeconds(108), section.NextDue);

        for (var page = 0; page < 20; page++)
        {
            section.GotThrough();
        }

        var again = later + TimeSpan.FromMinutes(30);
        Assert.True(section.TryStart(again));
        section.Throttled(TimeSpan.FromSeconds(60), again, again);
        Assert.InRange(section.Interval, TimeSpan.FromSeconds(26), TimeSpan.FromSeconds(27));
        Assert.Equal(again + TimeSpan.FromSeconds(60), section.NextDue);
    }

    [Fact]
    public void WithoutRetryAfterItStartsAtTenSecondsAndNeverGoesPastTwentyMinutes()
    {
        var section = new SectionPace(string.Empty);
        var now = T0;
        List<double> gaps = [];
        for (var trip = 0; trip < 10; trip++)
        {
            now = section.NextDue > now ? section.NextDue : now;
            Assert.True(section.TryStart(now));
            section.Throttled(null, now, now);
            gaps.Add(section.Interval.TotalSeconds);
        }

        // It keeps doubling past five minutes, so the last tries come after a long quiet spell: then 20 minutes, twice,
        // two 429s at the slowest pace so far, not yet three.
        double[] doubling = [10, 20, 40, 80, 160, 320, 640, 1_200, 1_200, 1_200];
        Assert.Equal(doubling, gaps);
        Assert.Equal(SectionPace.MAX_INTERVAL, section.Interval);
        Assert.Equal("/", section.Path);
        Assert.False(section.IsRefusingEverything(3));

        now = section.NextDue;
        Assert.True(section.TryStart(now));
        section.Throttled(null, now, now);
        Assert.True(section.IsRefusingEverything(3));
    }

    /// <summary>
    /// The crawl stops asking the section at a moment it sets: a turn due after that comes then instead, once, and one due
    /// sooner keeps its time.
    /// </summary>
    [Fact]
    public void ATurnDueAfterTheLastMomentComesThenInstead()
    {
        var section = new SectionPace("products");
        section.Throttled(TimeSpan.FromMinutes(16), T0, T0);
        var last = T0 + TimeSpan.FromMinutes(5);

        Assert.Equal(T0 + TimeSpan.FromMinutes(16), section.NextDueBy(null));
        Assert.Equal(last, section.NextDueBy(last));
        Assert.Equal(T0 + TimeSpan.FromMinutes(16), section.NextDueBy(T0 + TimeSpan.FromMinutes(30)));
        Assert.Null(section.LastTurn);

        Assert.False(section.TryStart(last - TimeSpan.FromSeconds(1), last));
        Assert.True(section.TryStart(last, last));
        Assert.Equal(last, section.LastTurn);
        Assert.Equal(last + TimeSpan.FromMinutes(16), section.NextDue);
    }

    /// <summary>
    /// The crawl's own requests to the section, sent before it was limited, come back 429 one after another: they don't
    /// each double the interval, which only the section's own turns do.
    /// </summary>
    [Fact]
    public void A429ForAPageTheSectionDidntLetGoOnlyWaitsOutWhatItAsks()
    {
        var section = new SectionPace("products");
        section.Throttled(TimeSpan.FromSeconds(2), T0, T0);

        for (var late = 1; late <= 5; late++)
        {
            var at = T0 + TimeSpan.FromMilliseconds(100 * late);
            section.Throttled(TimeSpan.FromSeconds(2), at, at);
        }

        Assert.Equal(TimeSpan.FromSeconds(2), section.Interval);
        Assert.Equal(T0 + TimeSpan.FromMilliseconds(2_500), section.NextDue);
    }

    [Fact]
    public void A429ForAPageSentBeforeTheLastOneOnlyWaitsOutWhatItAsks()
    {
        var section = new SectionPace("products");
        section.Throttled(TimeSpan.FromSeconds(10), T0, T0 + TimeSpan.FromSeconds(1));

        section.Throttled(TimeSpan.FromSeconds(30), T0, T0 + TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(10), section.Interval);
        Assert.Equal(1, section.FailuresInARow);
        Assert.Equal(T0 + TimeSpan.FromSeconds(32), section.NextDue);
    }

    /// <summary>
    /// 429s on product pages limit /products/ alone: the host's pace isn't halved, and its other pages go on. A product
    /// page under a collection is in the section too.
    /// </summary>
    [Fact]
    public void A429OnOneSectionLimitsItAloneAndTheHostGoesOn()
    {
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.FromMilliseconds(125), new FakeTimeProvider(T0));
        var before = gate.PaceInterval;

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(60), uri: new Uri("https://shop.example/products/belt"));

        Assert.Equal(before, gate.PaceInterval);
        Assert.Equal(TimeSpan.Zero, gate.ReadState().HeldFor);
        Assert.Equal("/products/", gate.LimitedSectionOf(new Uri("https://shop.example/products/gi"))?.Path);
        Assert.Equal("/products/", gate.LimitedSectionOf(new Uri("https://shop.example/collections/belts/products/black"))?.Path);
        Assert.Null(gate.LimitedSectionOf(new Uri("https://shop.example/collections/belts")));
        Assert.Null(gate.LimitedSectionOf(new Uri("https://shop.example/pages/faqs")));
        Assert.Null(gate.LimitedSectionOf(new Uri("https://shop.example/")));
    }

    [Fact]
    public void A429ForAPageInNoFolderLimitsTheRootPages()
    {
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, new FakeTimeProvider(T0));

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(5), uri: new Uri("https://shop.example/cart"));

        Assert.Equal("/", gate.LimitedSectionOf(new Uri("https://shop.example/"))?.Path);
        Assert.Null(gate.LimitedSectionOf(new Uri("https://shop.example/products/gi")));
    }

    /// <summary>429s from three sections at once are the host's limit, not a section's: the whole host slows and pauses.</summary>
    [Fact]
    public void A429SpreadOverThreeSectionsSlowsTheWholeHost()
    {
        var time = new FakeTimeProvider(T0);
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, time);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30), uri: new Uri("https://shop.example/products/a"));
        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30), uri: new Uri("https://shop.example/pages/b"));
        Assert.Equal(TimeSpan.Zero, gate.ReadState().HeldFor);

        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(30), uri: new Uri("https://shop.example/blogs/news/c"));

        Assert.Equal(TimeSpan.FromSeconds(30), gate.ReadState().HeldFor);
        Assert.NotNull(gate.ReadState().SlowedTo);
    }

    [Fact]
    public void APageThatGetsThroughSpeedsItsSectionUp()
    {
        var gate = new HostGate(maxConcurrency: 4, TimeSpan.Zero, new FakeTimeProvider(T0));
        var product = new Uri("https://shop.example/products/a");
        gate.Report(HostSignal.Throttled, TimeSpan.FromSeconds(60), uri: product);

        gate.Report(HostSignal.Success, uri: new Uri("https://shop.example/products/b"));

        Assert.Equal(TimeSpan.FromSeconds(54), gate.LimitedSectionOf(product)!.Interval);
    }
}
