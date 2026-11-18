namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Why a check was skipped (<see cref="Models.SkippedCheck.Reason"/>). The codes are stored in crawl files and keyed on
/// by the UI, so never change one; add new ones instead.
/// </summary>
public static class SkippedCheckReasons
{
    /// <summary>The crawl was told not to read sitemaps.</summary>
    public const string SITEMAPS_OFF = "sitemaps-off";

    /// <summary>The crawl was stopped (or failed) before it finished.</summary>
    public const string CRAWL_UNFINISHED = "crawl-unfinished";

    /// <summary>The crawl finished, but not every sitemap was read.</summary>
    public const string SITEMAPS_UNFINISHED = "sitemaps-unfinished";

    /// <summary>No sitemap could be read, so there's nothing to compare the crawl with.</summary>
    public const string NO_SITEMAP = "no-sitemap";

    /// <summary>A declared sitemap failed for a reason that says nothing about what it lists (a 5xx, a timeout, bad XML).</summary>
    public const string SITEMAP_UNREADABLE = "sitemap-unreadable";

    /// <summary>Sitemaps were left unread at the crawler's limits (too many, or nested too deep).</summary>
    public const string SITEMAPS_NOT_READ = "sitemaps-not-read";

    /// <summary>The depth limit kept internal URLs out of the crawl.</summary>
    public const string DEPTH_LIMIT = "depth-limit";

    /// <summary>The URL limit kept internal URLs out of the crawl.</summary>
    public const string URL_LIMIT = "url-limit";

    /// <summary>The crawl stayed in its start folder, so links from the rest of the site weren't read.</summary>
    public const string START_FOLDER = "start-folder";

    /// <summary>
    /// The crawl didn't start at the site's root (its start URL has a path other than <c>/</c>), so its first page isn't
    /// known to be the site's home page, which a check of the site as a whole is judged by.
    /// </summary>
    public const string START_NOT_ROOT = "start-not-root";

    /// <summary>
    /// At least 1 in 20 of the site's pages the crawl tried to read refused it (a firewall, a bot challenge, a rate limit)
    /// or gave no answer, so their links are unknown, and pages linked only from them would look orphaned.
    /// </summary>
    public const string PAGES_UNREAD = "pages-unread";

    /// <summary>The crawl was told not to check links to other sites.</summary>
    public const string EXTERNAL_LINKS_OFF = "external-links-off";

    /// <summary>The crawl was told not to fetch images and other files.</summary>
    public const string RESOURCES_OFF = "resources-off";

    /// <summary>
    /// The crawl was told not to follow hreflang links (the default), and a language version they name was left unread
    /// for it: a version nothing else leads to is one of those, so which are unlinked can't be told.
    /// </summary>
    public const string HREFLANG_OFF = "hreflang-off";

    /// <summary>
    /// The crawl was told to ignore robots.txt: it asked for every URL, so none was refused, and it read robots.txt only
    /// where sitemap discovery asked for it (the start URL's origin, with sitemaps on). What robots.txt blocks, and
    /// whether it answers where the crawl never read it, can't be judged.
    /// </summary>
    public const string ROBOTS_TXT_IGNORED = "robots-txt-ignored";

    /// <summary>
    /// The site's other versions (www or not, http or https) weren't all checked: the crawl stopped before they answered,
    /// it predates the crawler's probes, or the home page isn't a page.
    /// </summary>
    public const string SITE_VERSIONS_UNCHECKED = "site-versions-unchecked";

    /// <summary>
    /// The check's rule failed once every page was read, so the check has no results at all: a bug, or a crawl it can't
    /// cope with. The analysis log has the error, and the analysis is partial.
    /// </summary>
    public const string RULE_FAILED = "rule-failed";

    /// <summary>
    /// The crawl, or some of the pages the check needs, was made by an earlier version, which didn't record what the check
    /// compares (for near-duplicate content, the whole 256-bit fingerprint of each page). Crawling again mends it.
    /// </summary>
    public const string EARLIER_VERSION = "earlier-version";

    /// <summary>
    /// Never stored: the check is newer than the engine that ran the analysis, so the analysis didn't know it.
    /// <see cref="Storage.IssueReader.ReadSkippedChecks"/> reports it for such checks. A check that an analysis by a newer
    /// engine didn't run gets no reason: that engine dropped it.
    /// </summary>
    public const string NEWER_THAN_ANALYSIS = "newer-than-analysis";
}
