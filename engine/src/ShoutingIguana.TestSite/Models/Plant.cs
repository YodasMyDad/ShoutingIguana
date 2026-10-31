namespace ShoutingIguana.TestSite.Models;

/// <summary>A URL under /plants/ with known defects, and what a correct crawl and analysis report for it.</summary>
/// <param name="Status">The status code it answers with, or 0 when it drops the connection.</param>
/// <param name="Linked">Linked from the plants hub. Others are reached through a redirect, canonical or image.</param>
/// <param name="InSitemap">Listed in the extra sitemap. Only indexable pages are, so the sitemap itself stays clean.</param>
/// <param name="Issues">Issue ids the analysis reports for this URL.</param>
internal sealed record Plant(string Path, int Status, bool Linked, bool InSitemap, IReadOnlyList<string> Issues, Func<SyntheticResponse> Respond);
