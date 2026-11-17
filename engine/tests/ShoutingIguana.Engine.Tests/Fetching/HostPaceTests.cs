using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Tests.Fetching;

/// <summary>The pace a host is asked at, as its answers teach it: pure arithmetic on given times.</summary>
public sealed class HostPaceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartsWithOneRequestAtATimeAtOneASecond()
    {
        var pace = new HostPace(maxConcurrency: 8, maxRate: null);

        Assert.Equal(1, pace.Window);
        Assert.Equal(HostPace.START_RATE, pace.Rate);
        Assert.Equal(TimeSpan.FromSeconds(1), pace.Interval);
        Assert.Null(pace.SlowedTo);
    }

    [Fact]
    public void ACrawlSetSlowerThanTheStartStartsAtItsOwnRate()
    {
        var pace = new HostPace(maxConcurrency: 1, maxRate: 0.5);

        Assert.Equal(0.5, pace.Rate);
    }

    [Fact]
    public void EachHealthyAnswerDoublesTheRateAndAllowsOneMoreAtOnceUntilThereIsNoLimit()
    {
        var pace = new HostPace(maxConcurrency: 8, maxRate: null);

        var answers = 0;
        while (pace.Rate is not null)
        {
            Assert.Equal(Math.Pow(2, answers), pace.Rate);
            pace.Answered(HostSignal.Success, null, T0, T0);
            answers++;
        }

        // Past 256 a second it is no limit at all: a healthy site is at full speed after nine answers.
        Assert.Equal(9, answers);
        Assert.Equal(8, pace.Window);
        Assert.Equal(TimeSpan.Zero, pace.Interval);
        Assert.Null(pace.SlowedTo);
    }

    [Fact]
    public void TheSlowStartStopsAtTheCrawlsOwnSettings()
    {
        var pace = new HostPace(maxConcurrency: 2, maxRate: 5);

        AnswerHealthy(pace, 10, T0);

        Assert.Equal(5, pace.Rate);
        Assert.Equal(2, pace.Window);
        Assert.Null(pace.SlowedTo);
    }

    [Fact]
    public void AThrottleHalvesThePaceThatTrippedItAndAllowsOneRequestAtATime()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 8);
        var now = StartEvery(pace, TimeSpan.FromMilliseconds(125), 32, T0);

        var pause = pace.Answered(HostSignal.Throttled, null, now, now);

        Assert.Equal(4, pace.Rate);
        Assert.Equal(1, pace.Window);
        Assert.Equal(HostPace.DEFAULT_PAUSE, pause);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pace.SlowedTo);
    }

    /// <summary>The real case: no rate set, and the site tripped at the pace actually sent, about one a second.</summary>
    [Fact]
    public void WithNoRateSetItHalvesThePaceActuallySent()
    {
        var pace = AtFullSpeed(maxConcurrency: 5, maxRate: null);
        var now = StartEvery(pace, TimeSpan.FromMilliseconds(900), 20, T0);

        pace.Answered(HostSignal.Throttled, TimeSpan.FromSeconds(60), now, now);

        Assert.Equal(1 / 0.9 / 2, pace.Rate!.Value, precision: 6);
        Assert.Equal(1, pace.Window);
    }

    [Fact]
    public void RetryAfterSetsThePauseUpToTwoMinutes()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 10);

        Assert.Equal(TimeSpan.FromSeconds(60), pace.Answered(HostSignal.Throttled, TimeSpan.FromSeconds(60), T0, T0));
        Assert.Equal(HostPace.MAX_PAUSE, pace.Answered(HostSignal.Throttled, TimeSpan.FromHours(1), T0, T0));
    }

    [Fact]
    public void WithoutRetryAfterThePauseDoublesWhileTheHostKeepsAskingUpToTwoMinutes()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 10);
        var now = T0;
        List<TimeSpan> pauses = [];
        for (var trip = 0; trip < 6; trip++)
        {
            pauses.Add(pace.Answered(HostSignal.Throttled, null, now, now));
            now += TimeSpan.FromSeconds(1);
        }

        double[] doubling = [10, 20, 40, 80, 120, 120];
        Assert.Equal(doubling, pauses.Select(pause => pause.TotalSeconds));
    }

    /// <summary>Requests already out when the host asked were sent at the old pace: their 429s ask for nothing new.</summary>
    [Fact]
    public void AnswersToRequestsSentBeforeTheSlowdownDoNotSlowItAgain()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 10);
        var sentBefore = StartEvery(pace, TimeSpan.FromMilliseconds(100), 32, T0);
        var tripped = sentBefore + TimeSpan.FromMilliseconds(50);
        pace.Answered(HostSignal.Throttled, TimeSpan.FromSeconds(5), sentBefore, tripped);

        var pause = pace.Answered(HostSignal.Throttled, TimeSpan.FromSeconds(30), sentBefore, tripped + TimeSpan.FromMilliseconds(10));
        pace.Answered(HostSignal.Throttled, null, sentBefore, tripped + TimeSpan.FromMilliseconds(20));

        Assert.Equal(5, pace.Rate!.Value, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(30), pause);
    }

    [Fact]
    public void ItRecoversATenthOfThePaceItSlowedFromEveryThirtySecondsWithoutASlowdown()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 20);
        var tripped = StartEvery(pace, TimeSpan.FromMilliseconds(100), 32, T0);
        pace.Answered(HostSignal.Throttled, TimeSpan.FromSeconds(1), tripped, tripped);
        Assert.Equal(5, pace.Rate!.Value, precision: 6);

        // Healthy answers straight away don't bring it back: that was the burst that renewed a rate limit's penalty. The
        // thirty seconds are counted from the end of the pause.
        AnswerHealthy(pace, 50, tripped + TimeSpan.FromSeconds(30));
        Assert.Equal(5, pace.Rate!.Value, precision: 6);
        Assert.Equal(1, pace.Window);

        pace.Answered(HostSignal.Success, null, tripped, tripped + TimeSpan.FromSeconds(31));
        Assert.Equal(6, pace.Rate!.Value, precision: 6);
        Assert.Equal(2, pace.Window);

        pace.Answered(HostSignal.Success, null, tripped, tripped + TimeSpan.FromSeconds(46));
        Assert.Equal(6, pace.Rate!.Value, precision: 6);
        pace.Answered(HostSignal.Success, null, tripped, tripped + TimeSpan.FromSeconds(61));
        Assert.Equal(7, pace.Rate!.Value, precision: 6);
    }

    [Fact]
    public void ForTenMinutesAfterATripItStaysBelowThePaceThatTrippedIt()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 20);
        var tripped = StartEvery(pace, TimeSpan.FromMilliseconds(100), 32, T0);
        pace.Answered(HostSignal.Throttled, null, tripped, tripped);

        for (var step = 1; step < 20; step++)
        {
            pace.Answered(HostSignal.Success, null, tripped, tripped + step * HostPace.RECOVERY_STEP);
        }

        Assert.Equal(9, pace.Rate!.Value, precision: 6);
        Assert.Equal(1 / 9.0, pace.SlowedTo!.Value.TotalSeconds, precision: 6);

        pace.Answered(HostSignal.Success, null, tripped, tripped + HostPace.TRIP_MEMORY + HostPace.RECOVERY_STEP);
        Assert.Equal(10, pace.Rate!.Value, precision: 6);
    }

    [Fact]
    public void OnceRecoveredToTheCrawlsOwnRateItIsNoLongerSlowed()
    {
        var pace = AtFullSpeed(maxConcurrency: 2, maxRate: 2);
        var tripped = StartEvery(pace, TimeSpan.FromMilliseconds(500), 32, T0);
        pace.Answered(HostSignal.Throttled, null, tripped, tripped);
        Assert.Equal(TimeSpan.FromSeconds(1), pace.SlowedTo);

        for (var step = 1; step <= 30; step++)
        {
            pace.Answered(HostSignal.Success, null, tripped, tripped + step * HostPace.RECOVERY_STEP);
        }

        Assert.Equal(2, pace.Rate);
        Assert.Equal(2, pace.Window);
        Assert.Null(pace.SlowedTo);
    }

    [Fact]
    public void RepeatedTripsHalveItAgainDownToOneRequestEveryTenSeconds()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 10);
        var now = T0;
        for (var trip = 0; trip < 12; trip++)
        {
            pace.Answered(HostSignal.Throttled, null, now, now);
            now += TimeSpan.FromSeconds(1);
        }

        Assert.Equal(HostPace.MIN_RATE, pace.Rate);
        Assert.Equal(TimeSpan.FromSeconds(10), pace.Interval);
    }

    [Fact]
    public void TwoFailuresInARowSlowItWithoutAPauseAtMostOnceEveryThirtySeconds()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 40);
        var now = StartEvery(pace, TimeSpan.FromMilliseconds(25), 32, T0);

        Assert.Equal(TimeSpan.Zero, pace.Answered(HostSignal.Failed, null, now, now));
        Assert.Equal(40, pace.Rate);
        Assert.Equal(TimeSpan.Zero, pace.Answered(HostSignal.Failed, null, now, now));
        Assert.Equal(20, pace.Rate!.Value, precision: 6);
        Assert.Equal(1, pace.Window);

        pace.Answered(HostSignal.Failed, null, now, now + TimeSpan.FromSeconds(1));
        pace.Answered(HostSignal.Failed, null, now, now + TimeSpan.FromSeconds(1));
        Assert.Equal(20, pace.Rate!.Value, precision: 6);
    }

    [Fact]
    public void ASingleFailureAmongHealthyAnswersDoesNotSlowIt()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: 10);
        foreach (var signal in new[] { HostSignal.Failed, HostSignal.Success, HostSignal.Failed, HostSignal.Success, HostSignal.Failed })
        {
            pace.Answered(signal, null, T0, T0);
        }

        Assert.Equal(10, pace.Rate);
        Assert.Equal(4, pace.Window);
    }

    [Fact]
    public void FailuresNeverSlowItBelowOneASecondNorLiftAPaceAlreadyBelowThat()
    {
        var pace = AtFullSpeed(maxConcurrency: 4, maxRate: null);
        var now = StartEvery(pace, TimeSpan.FromSeconds(1), 32, T0);
        pace.Answered(HostSignal.Failed, null, now, now);
        pace.Answered(HostSignal.Failed, null, now, now);
        Assert.Equal(HostPace.START_RATE, pace.Rate);

        now += HostPace.RECOVERY_STEP;
        pace.Answered(HostSignal.Throttled, null, now, now);
        Assert.Equal(0.5, pace.Rate);
        now += HostPace.RECOVERY_STEP + TimeSpan.FromSeconds(1);
        pace.Answered(HostSignal.Failed, null, now, now);
        pace.Answered(HostSignal.Failed, null, now, now);
        Assert.Equal(0.5, pace.Rate);
    }

    [Fact]
    public void AFirewallsBlockPageNeitherSlowsNorSpeedsIt()
    {
        var pace = new HostPace(maxConcurrency: 4, maxRate: null);

        pace.Answered(HostSignal.Refused, null, T0, T0);
        pace.Answered(HostSignal.Refused, null, T0, T0);

        Assert.Equal(HostPace.START_RATE, pace.Rate);
        Assert.Equal(1, pace.Window);
        Assert.Null(pace.SlowedTo);
    }

    [Fact]
    public void AnswersThatSlowWellPastTheHostsUsualTimeHoldTheSlowStartForAFewAnswers()
    {
        var pace = new HostPace(maxConcurrency: 16, maxRate: null);
        AnswerIn(pace, 5, TimeSpan.FromMilliseconds(100));
        Assert.Equal(32, pace.Rate);

        // One slow answer is smoothed over; the next seven hold the pace.
        AnswerIn(pace, 1, TimeSpan.FromSeconds(3));
        Assert.Equal(64, pace.Rate);
        AnswerIn(pace, 7, TimeSpan.FromSeconds(3));
        Assert.Equal(64, pace.Rate);

        // Slow for that long without asking for less, it is how the host answers: the pace grows again.
        AnswerIn(pace, 1, TimeSpan.FromSeconds(3));
        Assert.Equal(128, pace.Rate);
        AnswerIn(pace, 2, TimeSpan.FromSeconds(3));
        Assert.Null(pace.Rate);
    }

    [Fact]
    public void AnswersBackAtTheUsualSpeedEndTheHold()
    {
        var pace = new HostPace(maxConcurrency: 16, maxRate: null);
        AnswerIn(pace, 5, TimeSpan.FromMilliseconds(100));
        AnswerIn(pace, 4, TimeSpan.FromSeconds(3));
        var held = pace.Rate;

        AnswerIn(pace, 10, TimeSpan.FromMilliseconds(100));

        Assert.True(held < 256, $"held at {held}");
        Assert.Null(pace.Rate);
    }

    [Theory]
    [InlineData(500, "2 requests a second")]
    [InlineData(1000, "1 request a second")]
    [InlineData(2000, "0.5 requests a second")]
    [InlineData(10_000, "one request every 10 seconds")]
    public void SaysThePaceInWords(int intervalMs, string words) =>
        Assert.Equal(words, HostPace.DescribePace(TimeSpan.FromMilliseconds(intervalMs)));

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(60, "60 seconds")]
    [InlineData(120, "2 minutes")]
    [InlineData(150, "2 min 30 s")]
    public void SaysAWaitInWords(int seconds, string words) => Assert.Equal(words, HostPace.DescribeWait(TimeSpan.FromSeconds(seconds)));

    private static HostPace AtFullSpeed(int maxConcurrency, double? maxRate)
    {
        var pace = new HostPace(maxConcurrency, maxRate);
        AnswerHealthy(pace, 12, T0);
        Assert.Equal(maxRate, pace.Rate);
        Assert.Equal(maxConcurrency, pace.Window);
        return pace;
    }

    private static void AnswerIn(HostPace pace, int answers, TimeSpan latency)
    {
        for (var answer = 0; answer < answers; answer++)
        {
            pace.Answered(HostSignal.Success, null, T0, T0, latency);
        }
    }

    private static void AnswerHealthy(HostPace pace, int answers, DateTimeOffset at)
    {
        for (var answer = 0; answer < answers; answer++)
        {
            pace.Answered(HostSignal.Success, null, at, at);
        }
    }

    /// <summary>Starts <paramref name="count"/> requests <paramref name="interval"/> apart from <paramref name="from"/>; returns the last start.</summary>
    private static DateTimeOffset StartEvery(HostPace pace, TimeSpan interval, int count, DateTimeOffset from)
    {
        var at = from;
        for (var start = 0; start < count; start++)
        {
            at = from + start * interval;
            pace.Started(at);
        }

        return at;
    }
}
