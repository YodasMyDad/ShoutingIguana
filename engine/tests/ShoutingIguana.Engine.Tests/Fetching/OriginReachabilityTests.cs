using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class OriginReachabilityTests
{
    [Fact]
    public void TheThirdUrlInARowWithoutAnAnswerCallsForAVerdictOnce()
    {
        var origin = new OriginReachability();

        var verdicts = Enumerable.Range(0, HostGate.UNREACHABLE_AFTER_URLS + 2).Select(_ => origin.ReportUrl(answered: false)).ToList();

        Assert.Equal([false, false, true, false, false], verdicts);
        Assert.False(origin.IsUnreachable);
    }

    [Fact]
    public void AnAnswerEndsUnreachable()
    {
        var origin = Tripped();
        origin.MarkUnreachable();
        Assert.True(origin.IsUnreachable);

        origin.ReportUrl(answered: true);

        Assert.False(origin.IsUnreachable);
    }

    [Fact]
    public void AnAnswerWhileTheOriginIsJudgedKeepsItReachable()
    {
        var origin = Tripped();

        origin.ReportUrl(answered: true);
        origin.MarkUnreachable();

        Assert.False(origin.IsUnreachable);
    }

    [Fact]
    public void AfterAResetItTakesAsManyUrlsAgain()
    {
        var origin = Tripped();

        origin.Reset();

        Assert.False(origin.ReportUrl(answered: false));
        Assert.False(origin.ReportUrl(answered: false));
        Assert.True(origin.ReportUrl(answered: false));
    }

    private static OriginReachability Tripped()
    {
        var origin = new OriginReachability();
        for (var url = 0; url < HostGate.UNREACHABLE_AFTER_URLS; url++)
        {
            origin.ReportUrl(answered: false);
        }

        return origin;
    }
}
