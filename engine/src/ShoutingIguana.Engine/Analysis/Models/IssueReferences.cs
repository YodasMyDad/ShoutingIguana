namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// How the URLs a check flags stand to the references between URLs (links, images, canonicals, hreflang, redirects,
/// sitemap entries), so a host can show where to fix each one. Only how a check is shown: it isn't stored with an
/// analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
/// </summary>
public enum IssueReferences
{
    /// <summary>Neither: the fault is in the flagged URL itself (its title, its size), or in the site as a whole.</summary>
    None,

    /// <summary>
    /// The flagged URL is something other URLs refer to (a 404, a redirect, a URL robots.txt blocks, a sitemap entry):
    /// what needs fixing is where it's found, the pages that link to it and the sitemaps that list it
    /// (<see cref="Storage.FoundOnReader.ReadFoundOn"/>).
    /// </summary>
    Sources,

    /// <summary>
    /// The flagged URL is the page that holds the bad references: its issue names the URLs it refers to (each broken
    /// target, the canonical, the redirecting link's target), which <see cref="Storage.FoundOnReader.ReadTargets"/> lists.
    /// </summary>
    Targets
}
