namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>One entry in the issue catalog.</summary>
/// <param name="Id">Stable kebab-case id stored in crawl files, e.g. <c>title-too-long</c>.</param>
/// <param name="Description">What is wrong. The catalog text's "what".</param>
/// <param name="Fix">What to do about it. The catalog text's "fix".</param>
public sealed record IssueDefinition(
    string Id,
    IssueCategory Category,
    IssueSeverity Severity,
    string Title,
    string Description,
    string Fix)
{
    /// <summary>
    /// Why it matters. Shown with the check, and not part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>
    /// A page that explains the check, or null when no authoritative source exists. Not part of
    /// <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public string? LearnMore { get; init; }

    /// <summary>
    /// How the check's issues group, as its rule writes their details. Only how they're shown: it isn't stored with an
    /// analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required IssueGrouping Grouping { get; init; }

    /// <summary>
    /// What the URLs the check flags are: always pages, or any URL the crawl has, as its rule can flag them. Only how its
    /// count is worded: it isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required IssueSubject Subject { get; init; }

    /// <summary>
    /// How the URLs the check flags stand to the references between URLs: each one is found somewhere (a 404 the site
    /// links to: show where), it holds the bad references (a page linking to broken URLs: show them), or neither. Only how
    /// the check is shown: it isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public IssueReferences References { get; init; }

    /// <summary>
    /// The URL grid's columns (<c>Storage.UrlColumns</c> ids) that show what the check found on each of its rows, in the
    /// order to show them: a too-long title's title and its length, where a noindex comes from, a redirect's hops. Empty
    /// for a check whose URL and detail say it all. Only how the check is shown: it isn't stored with an analysis, nor
    /// part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>
    /// What to say about each URL the check's issues name (a page's targets, a group's related URL), as the names of a
    /// target's fields (<c>Storage.Models.IssueTarget</c>) in the order to say them: an oversized image's file size and
    /// pixels, a redirecting link's final URL. Empty for a check that names no URL. Only how the check is shown, as
    /// <see cref="Evidence"/> is.
    /// </summary>
    public IReadOnlyList<string> TargetFacts { get; init; } = [];

    /// <summary>
    /// The checks a fix of this one also clears, by id: this check flags the URLs others refer to (<see cref="References"/>
    /// is <see cref="IssueReferences.Sources"/>: a 404, a redirect chain), and each of those flags the pages that refer to
    /// them (<see cref="IssueReferences.Targets"/>: links to broken URLs, links to redirects). A fix of the URL, or of its
    /// references as the fix says, clears the pages whose every target is one of this check's URLs; how many that is on a
    /// crawl is counted from its issues (<c>Storage.IssueCauseReader</c>), never assumed. Only how the check is shown: it
    /// isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public IReadOnlyList<string> Clears { get; init; } = [];

    /// <summary>
    /// What a recheck of the check (the fix loop's "Recheck these N pages") reads again to settle it: its URLs and what
    /// judges them, every sitemap, the robots.txt of the origins it names, or the site's other versions. Empty for a check
    /// a recheck can't settle, because it depends on pages nobody can name in advance: an orphan (only a full crawl shows
    /// that something now links to it) and a site not on HTTPS (moving changes every address). Required, so no check is
    /// added without saying. Only what a recheck does: it isn't stored with an analysis, nor part of
    /// <see cref="AnalysisFreshness.ChecksFingerprint"/>.
    /// </summary>
    public required IReadOnlyList<RecheckKind> Recheck { get; init; }

    /// <summary>
    /// The check finds one fault of the whole site, not a fault of pages: robots.txt that search engines can't read, a
    /// site not on HTTPS. However many URLs it is reported on (one, or each other version of the site that answers), the
    /// score counts it as the whole site (<see cref="ScoreCalculator"/>). It isn't part of
    /// <see cref="AnalysisFreshness.ChecksFingerprint"/>: a change to it, or to <see cref="Subject"/>, changes what a
    /// score is worked out from, so it comes with a new <see cref="ScoreCalculator.VERSION"/>.
    /// </summary>
    public bool SiteWide { get; init; }

    /// <summary>
    /// The check's rule reports one issue for the whole site, on the URL that stands for it (the home page, or the start
    /// URL without one), or one for each other version of the site: never one for each page or URL at fault. So a host
    /// can say "checked once, for the whole site" where "1 of 265 pages" would misread. Only how its count is worded: it
    /// isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>, and the score never
    /// reads it. Every <see cref="SiteWide"/> check is reported once; not every check reported once is a fault of the
    /// whole site (a sitemap that can't be read, a missing security header).
    /// </summary>
    public bool ReportedOnce { get; init; }

    /// <summary>
    /// The kind of file a check of the whole site is about (robots.txt, a sitemap), whose rule names the files at fault
    /// (<see cref="IssueHit.Files"/>); null for a check of pages or URLs. Only how the check is shown.
    /// </summary>
    public IssueAbout? About { get; init; }

    /// <summary>
    /// The check in plain words (<see cref="IssuePlainTexts"/>), or null while it has none, as written: a limit in them
    /// is still its token, which <see cref="PlainLimits"/> fills from an analysis's settings. Not part of
    /// <see cref="AnalysisFreshness.ChecksFingerprint"/>, nor of what an analysis stores.
    /// </summary>
    public IssuePlainText? Plain => IssuePlainTexts.Find(Id);

    /// <summary>
    /// For a check grouped by detail whose detail is each page's own instance of a fault (the href of its relative canonical,
    /// a JSON parser's message saying where in the block it failed): the fault the detail shows, which its groups are by
    /// instead, so a fault in the site's template is one group. The analysis stores it beside each issue, and the issue keeps
    /// its own detail, so a change to one comes with a new <see cref="AnalysisFreshness.VERSION"/>. Null groups by the
    /// detail itself.
    /// </summary>
    internal Func<string, string>? FaultOf { get; init; }
}
