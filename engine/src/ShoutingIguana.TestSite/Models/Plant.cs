namespace ShoutingIguana.TestSite.Models;

/// <summary>A URL with known defects, and what a correct crawl and analysis report for it.</summary>
/// <param name="Status">The status code it answers with, or 0 when it drops the connection.</param>
/// <param name="Linked">Linked from the plants hub. Others are reached through a redirect, canonical, iframe or image.</param>
/// <param name="InSitemap">
/// Listed in the extra sitemap. Only indexable pages are, so the sitemap itself stays clean, apart from partner-site
/// plants that stand for entries on another host.
/// </param>
/// <param name="Issues">Issue ids the analysis reports for this URL.</param>
/// <param name="IsPage">A 200 HTML page the crawler parses. An iframe's page is only status-checked.</param>
/// <param name="IsAnalysed">A parsed page the analysis audits. Cloudflare's /cdn-cgi/ endpoints aren't.</param>
/// <param name="OnPartnerSite">
/// Served by the partner site, the second origin that stands for other sites, so the crawler only status-checks it.
/// Its URL is the partner origin plus <see cref="Path"/>.
/// </param>
/// <param name="Url">The absolute URL to link, list and expect issues on, when it isn't the site's base plus the path (an http:// page of the HTTPS site).</param>
internal sealed record Plant(
    string Path,
    int Status,
    bool Linked,
    bool InSitemap,
    IReadOnlyList<string> Issues,
    Func<SyntheticResponse> Respond,
    bool IsPage = true,
    bool IsAnalysed = true,
    bool OnPartnerSite = false,
    string? Url = null);
