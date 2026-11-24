using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Serialization;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// Whether a crawl's last analysis still stands: the crawl hasn't changed since, this engine would judge it the same
/// way, and it would run with the same thresholds. Each analysis stores what it was run against for this.
/// </summary>
public static class AnalysisFreshness
{
    /// <summary>
    /// What an analysis computes and stores. Bump it with every change that alters what analysing the same crawl
    /// stores: a rule that judges differently, a changed threshold default, a new column in the URL summary. Analyses
    /// stored by another version are out of date (<see cref="OutOfDateReason.EngineChanged"/>), and the UI offers to
    /// run them again. A change to the catalog's checks is caught without a bump, by <see cref="ChecksFingerprint"/>.
    /// 4 stores every other URL an issue is about and the fault some checks group by (<c>issue_targets</c>,
    /// <c>issue_faults</c>), and indexes the links to a URL by kind. 5 also judges near duplicates on four SimHash
    /// lanes (256 bits, within <c>NearDuplicateMaxBits</c>) and skips that check when a crawl didn't record them.
    /// 6 drops the FAQPage rich-result requirement (Google removed FAQ rich results), rejects UN M.49 hreflang
    /// regions, parses JSON-LD strictly, requires a JobPosting location or <c>jobLocationType</c>, flags HTML past
    /// Googlebot's crawl limit, and changes the severity of <c>blocked-by-robots</c>, <c>description-missing</c>,
    /// <c>h1-missing</c> and <c>duplicate-site-version</c>. 7 gives a status code its name in details ("404 Not Found" where
    /// 6 wrote "HTTP 404"), and stores the URLs crawled and the site's score with the analysis. 8 reports
    /// <c>site-hidden-from-search</c> only when the home page is noindex too, skips <c>blocked-by-robots</c> on a crawl
    /// that ignored robots.txt, names the URL a redirecting canonical ends at in <c>canonical-to-redirect</c>'s detail, and
    /// passes a page that declares its encoding with a byte-order mark (which only a crawl that recorded the mark shows).
    /// 9 was used twice, by two lines of work merged later. One is the real-site check audit's (docs/v2-check-audit.md):
    /// a canonical or hreflang alternate that refused the crawler, or an alternate another site's robots.txt disallows,
    /// isn't non-indexable; links to temporary redirects aren't links to redirects, and a temporary redirect to another
    /// site isn't flagged; a page's empty links to itself count (not ones written as a fragment); orphans are skipped when
    /// 1 in 20 of the pages couldn't be read; schema.org subtypes need their parent's properties; and a meta charset past
    /// the first 1024 bytes declares nothing (which only a crawl that recorded where it ends shows). The other names the
    /// images at fault as the image checks' targets (they group by image), names in a detail what it counts (each title,
    /// H1, canonical, missing section, a loop's hops) and the limit a measurement is over, and counts the pages that use
    /// each URL in the URL summary (<c>used_on</c>). 10 is both together, so an analysis either 9 stored is out of date.
    /// 11 is the polite crawl's: <c>internal-blocked</c> is a notice for a firewall or bot protection's refusal only, as a
    /// 429 is held back and asked again, and stored as not checked if it never gets through; and the notes tell the URLs
    /// not checked for a site asking to slow down, or for a private network address, apart from those whose site stopped
    /// answering. Pages stored as not checked for a site asking to slow down still count as refused when the orphan check
    /// is skipped for the pages the crawl couldn't read. 12 measures titles and descriptions in pixels, as Google's desktop
    /// results show them (Arial at 20 px and 14 px, <c>titleMaxPixels</c> and the rest), with details such as
    /// <c>612 px, over 580 (64 characters)</c>, and stores <c>title_width</c> and <c>description_width</c> in the URL
    /// summary, with <c>title_length</c> and <c>description_length</c> in plain characters. 13 is the new checks' (step 3b,
    /// docs/v2-new-checks.md): a page's Refresh header is a refresh as its meta tag is, and an instant one makes it a
    /// redirect; a canonical to another site, or from a later page of a series to its first, and the pages of a canonical
    /// loop, are no longer reported as canonicalised or canonicalised to a non-indexable page; and a robots rule written
    /// with a space after its colon is one rule. 14 is the new sitemap checks' (batch 4): a sitemap entry on another scheme
    /// than every sitemap listing it, and not on the site's own, is reported as on another host (sitemap-url-other-host). 15
    /// writes one note for each reason checks weren't run, the checks totalled ("24 checks weren't run: this crawl was made
    /// by an earlier version…"), where 14 wrote one for each collection of facts a page's parse lacked. 16 is the "can I fix it
    /// from this?" pass's: the checks of the site's robots.txt and sitemaps store the files they're about (<c>issue_files</c>),
    /// <c>blocked-by-robots</c> names the rule that refused a URL and its line ("Disallow: /checkout, line 50"), a URL in
    /// several sitemaps names them, a wrong lastmod names the first entry and what it says, and an empty robots.txt says so. 17 marks the
    /// checks of what a Shopify store's platform set as Shopify's (<c>issue_types.platform</c>: its security headers and
    /// its own cookies, on pages Shopify served), which are then no part of the score or Fix first, and its notes say
    /// Shopify's reason and the fix for the pages a store refused, where 16 said to check again later or at the Polite speed.
    /// 18 is the page part's: it quotes in a detail what the check found: the heading
    /// too long (<c>H2 of 86 characters, over 70: "…"</c>) or skipped to (<c>H1 → H4: "…"</c>), the values of a repeated share
    /// tag, which image has no source or is inline, the line of a second &lt;head&gt; or &lt;body&gt;, and which JSON-LD block
    /// failed, on which line and where (counted from 1); and groups <c>heading-levels-skipped</c>, <c>social-tags-multiple</c>
    /// and <c>html-structure-errors</c> by the fault their detail shows (each from its <see cref="Models.IssueDefinition.FaultsSince"/>).
    /// 19 (the links part's) names each URL
    /// <c>protocol-relative-links</c> counts as the issue's targets (on pages parsed from collection 7 on), names in its detail
    /// any in a tag that isn't a link or file, and doesn't flag an HTTPS page the home page's HSTS covers; and
    /// <c>uncrawlable-links</c>' detail says where on the page each example is. 20 reads a security header sent twice, or
    /// with its repeats joined by commas, as browsers do (<c>X-Content-Type-Options: nosniff, nosniff</c> is nosniff, and
    /// frame-ancestors in any Content-Security-Policy line stands in for X-Frame-Options), and groups
    /// <c>canonical-loop</c> by the loop (<see cref="IssueFaults.CanonicalLoop"/>), so the pages of one loop are one group.
    /// 21 doesn't run the checks of what the sitemaps list on a crawl file that lost a sitemap's entries (its row counts
    /// URLs and the file holds none: a recheck by the engine of 18 November 2026 deleted them, then failed), where 20 read
    /// the empty list and reported every page as missing from the sitemaps (<see cref="SkippedCheckReasons.SITEMAP_LIST_LOST"/>).
    /// 22 drops the fix as code and says, in words, how to fix the checks that had one, and passes a legacy image whose
    /// picture element offers another source. An analysis 21 stored names checks whose texts have changed, and flagged
    /// those images, so it's out of date. 23 is JavaScript rendering. A crawl fetches by HTTP and, where the page is a
    /// client-rendered shell or the crawl lists its URL to render, parses the HTML the browser produced. Analysing a
    /// stored crawl is unchanged: the checks read the facts the crawl stored. The version moves so an analysis stored
    /// before this engine is out of date.
    /// 24 is the browser fallback. When a crawl turns it on, a host that blocks plain HTTP and whose browser fetch gets
    /// past the block stores that HTML, parsed once, where an earlier engine stored the block. Analysing a stored crawl
    /// is unchanged: the checks read the facts the crawl stored. The version moves so an analysis stored before this
    /// engine is out of date.
    /// 25 is the check audit of 2026-11-24 (docs/v2-check-audit.md): a canonical link with hreflang, lang, media or type is
    /// no canonical, and some twenty checks stopped flagging correct markup or missing real faults (a Sitemap line read as
    /// the parser reads it, equivalent language codes, digit path segments, every utm_ parameter, CSRF cookies,
    /// upgrade-insecure-requests, every JavaScript media type, an image in a picture, more image hosts, an offer's
    /// priceSpecification, readability over prose alone). canonical-invalid-attribute became a Warning.
    /// </summary>
    public const int VERSION = 25;

    /// <summary>
    /// The catalog's check ids with their categories and severities, hashed. Titles, descriptions, fixes, why and
    /// learn-more links don't count.
    /// </summary>
    public static string ChecksFingerprint { get; } = Fingerprint(IssueCatalog.All);

    /// <summary>Why the analysis no longer stands, or nothing when it does.</summary>
    /// <param name="crawlUpdatedAt">The crawl's <see cref="CrawlFileSummary.UpdatedAt"/> now.</param>
    /// <param name="settings">The thresholds an analysis would run with now.</param>
    public static IReadOnlyList<OutOfDateReason> Check(AnalysisInfo analysis, DateTimeOffset crawlUpdatedAt, AnalysisSettings settings)
    {
        List<OutOfDateReason> reasons = [];

        // Every run of a crawl stamps the crawl row when it starts and ends, and the analysis keeps the stamp it saw, so
        // comparing the two is exact whatever the clocks did. Engines that didn't keep it leave the times to compare.
        var crawlChanged = analysis.CrawlUpdatedAt is { } seen ? seen != crawlUpdatedAt : crawlUpdatedAt > analysis.AnalyzedAt;
        if (crawlChanged)
        {
            reasons.Add(OutOfDateReason.CrawlChanged);
        }

        // The score is the analysis's too: one worked out by another version of the formula isn't the score it would get now.
        if (analysis.AnalysisVersion != VERSION || analysis.ChecksFingerprint != ChecksFingerprint || analysis.ScoreVersion != ScoreCalculator.VERSION)
        {
            reasons.Add(OutOfDateReason.EngineChanged);
        }

        if (analysis.Settings is not { } used || Canonical(used) != Canonical(settings))
        {
            reasons.Add(OutOfDateReason.SettingsChanged);
        }

        return reasons;
    }

    /// <summary>
    /// Order-independent, so moving a check in the catalog changes nothing, while adding, removing or recategorising one,
    /// or changing its severity (which the issue counts are grouped by), does.
    /// </summary>
    internal static string Fingerprint(IEnumerable<IssueDefinition> checks)
    {
        var lines = checks.Select(check => $"{check.Id}\t{check.Category}\t{check.Severity}\n").Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines))))[..16];
    }

    /// <summary>
    /// The settings as the analysis stores them, so equal settings give equal text. Read back from a crawl file, a threshold
    /// the analysing engine didn't have takes its default. A new threshold that changes what the checks find comes with a
    /// new <see cref="VERSION"/>, so such an analysis is out of date anyway; one that doesn't leaves it standing, unless
    /// the threshold is set away from its default now.
    /// </summary>
    internal static string Canonical(AnalysisSettings settings) => JsonSerializer.Serialize(settings, EngineJsonContext.Default.AnalysisSettings);
}
