using ShoutingIguana.Engine.Crawling;
using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Tests.Crawling;

/// <summary>
/// A crawl's waits stay a handful whatever its size: with every subdomain included, hundreds of hosts can share one
/// robots.txt's crawl-delay, and each progress report goes to every UI twice a second.
/// </summary>
public sealed class CrawlWaitsTests
{
    [Fact]
    public void EachReasonListsItsLongestFiveAndCountsTheRest()
    {
        List<CrawlWait> waits =
        [
            .. Enumerable.Range(1, 217).Select(host => new CrawlWait(CrawlWaitReason.CrawlDelay, $"shop{host}.example.com", Duration: TimeSpan.FromSeconds(host % 10))),
            new(CrawlWaitReason.Backoff, "example.com", Duration: TimeSpan.FromMilliseconds(400)),
            .. Enumerable.Range(1, 6).Select(host => new CrawlWait(CrawlWaitReason.RobotsTxt, $"blog{host}.example.com", Urls: host)),
            new(CrawlWaitReason.OtherSitesRobotsTxt, Urls: 40)
        ];

        var (listed, more) = CrawlWaits.Cap(waits);

        var delays = listed.Where(wait => wait.Reason == CrawlWaitReason.CrawlDelay).ToList();
        Assert.Equal(CrawlWaits.MAX_PER_REASON, delays.Count);
        Assert.All(delays, wait => Assert.Equal(TimeSpan.FromSeconds(9), wait.Duration));
        Assert.Equal(["blog6.example.com", "blog5.example.com", "blog4.example.com", "blog3.example.com", "blog2.example.com"],
            listed.Where(wait => wait.Reason == CrawlWaitReason.RobotsTxt).Select(wait => wait.Host));
        Assert.Single(listed, wait => wait.Reason == CrawlWaitReason.Backoff);
        Assert.Single(listed, wait => wait.Reason == CrawlWaitReason.OtherSitesRobotsTxt);
        Assert.Equal([(CrawlWaitReason.RobotsTxt, 1), (CrawlWaitReason.CrawlDelay, 212)], more.Select(entry => (entry.Key, entry.Value)).Order());
    }

    [Fact]
    public void AFewWaitsAreListedAsTheyAre()
    {
        List<CrawlWait> waits =
        [
            new(CrawlWaitReason.HeldBack, "example.com", Duration: TimeSpan.FromSeconds(30)),
            new(CrawlWaitReason.SiteDown, "https://example.com", Duration: TimeSpan.FromMinutes(2))
        ];

        var (listed, more) = CrawlWaits.Cap(waits);

        Assert.Equal(waits, listed);
        Assert.Empty(more);
        Assert.Empty(CrawlWaits.Cap([]).Listed);
    }
}
