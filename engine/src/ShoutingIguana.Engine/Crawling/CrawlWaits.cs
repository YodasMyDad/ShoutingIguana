using ShoutingIguana.Engine.Crawling.Models;

namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// Keeps a crawl's waits to a handful, however many hosts it has: a site with every subdomain included can have hundreds
/// under one crawl-delay, and each progress report goes to every UI twice a second.
/// </summary>
internal static class CrawlWaits
{
    /// <summary>At most this many waits are listed for one reason; the rest are only counted.</summary>
    public const int MAX_PER_REASON = 5;

    /// <summary>
    /// The longest <see cref="MAX_PER_REASON"/> of each reason (the longest wait, or the most URLs waiting), by reason, and
    /// for each reason with more, how many more.
    /// </summary>
    public static (IReadOnlyList<CrawlWait> Listed, IReadOnlyDictionary<CrawlWaitReason, int> More) Cap(IReadOnlyList<CrawlWait> waits)
    {
        if (waits.Count == 0)
        {
            return ([], new Dictionary<CrawlWaitReason, int>());
        }

        List<CrawlWait> listed = [];
        Dictionary<CrawlWaitReason, int> more = [];
        foreach (var reason in waits.GroupBy(wait => wait.Reason).OrderBy(group => group.Key))
        {
            var longestFirst = reason
                .OrderByDescending(wait => wait.Duration ?? TimeSpan.Zero)
                .ThenByDescending(wait => wait.Urls ?? 0)
                .ThenBy(wait => wait.Host, StringComparer.Ordinal)
                .ToList();
            listed.AddRange(longestFirst.Take(MAX_PER_REASON));
            if (longestFirst.Count > MAX_PER_REASON)
            {
                more[reason.Key] = longestFirst.Count - MAX_PER_REASON;
            }
        }

        return (listed, more);
    }
}
