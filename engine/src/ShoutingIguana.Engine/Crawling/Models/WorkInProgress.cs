namespace ShoutingIguana.Engine.Crawling.Models;

/// <summary>What the crawl's workers are doing at one moment, read as a whole, so the counts agree with each other.</summary>
/// <param name="SiteWaitingForRobotsTxt">The site's own URLs waiting for their origin's robots.txt before they can be asked for.</param>
/// <param name="OthersWaitingForRobotsTxt">URLs on other sites (status checks of links to them) waiting for those sites' robots.txt.</param>
/// <param name="Fetching">URLs past robots.txt and being fetched: waiting on their host's spacing, being sent, or between retries.</param>
internal readonly record struct WorkInProgress(int SiteWaitingForRobotsTxt, int OthersWaitingForRobotsTxt, int Fetching)
{
    public int WaitingForRobotsTxt => SiteWaitingForRobotsTxt + OthersWaitingForRobotsTxt;
}
