namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// How the report is cut, whose words it is in, and how many of each problem's pages it lists; the rest are in the
/// workbook. A problem lists every one of its pages while they fit: a reader isn't sent to a second file for three rows.
/// </summary>
public sealed record ReportOptions
{
    public const int DEFAULT_EXAMPLES = 25;

    public const int DEFAULT_LOOK_EXAMPLES = 10;

    /// <summary>The most examples a problem can be asked to list: the report explains, and the workbook lists.</summary>
    public const int MAX_EXAMPLES = 50;

    public ReportCut Cut { get; init; } = ReportCut.Full;

    /// <summary>Whose words the rows are in: a site owner's (Simple), or a working SEO's (Advanced).</summary>
    public ReportWording Wording { get; init; } = ReportWording.Plain;

    /// <summary>The first pages listed for each serious problem and each problem worth fixing, in crawl order.</summary>
    public int Examples { get; init; } = DEFAULT_EXAMPLES;

    /// <summary>The first pages listed for each thing worth a look.</summary>
    public int LookExamples { get; init; } = DEFAULT_LOOK_EXAMPLES;

    /// <summary>
    /// Checks the user hid (by id), which the report and the workbook leave out of their lists and counts (discovery
    /// decision 2: "the analysis still runs it, and exports leave it out"). The score stays the analysis's, and the
    /// document says how many it left out. An id the catalog doesn't have is ignored: a check hidden by another version.
    /// </summary>
    public IReadOnlyList<string> HiddenChecks { get; init; } = [];

    /// <summary>
    /// The checks a view listed (by id), for the workbook of that view: only these are its rows and sheets. Null for
    /// every check.
    /// </summary>
    public IReadOnlyList<string>? Checks { get; init; }

    /// <summary>The most checks either list takes: more than the catalog has, few enough to keep a request small.</summary>
    public const int MAX_CHECKS = 500;

    /// <summary>
    /// The fix loop: the file of another crawl of the site to compare with. The report gains "Since your last check" after
    /// its summary (a line of it in the summary cut) and says what it was compared with under About this check; the
    /// workbook's summary gains each check's count then and its change, and a sheet of what was fixed. Null compares with
    /// nothing.
    /// </summary>
    public string? CompareWith { get; init; }

    /// <summary>
    /// Why a comparison that was asked for can't be made (the other crawl is of another site, say), said under About this
    /// check in place of one: the report is never refused for it. Null when there is nothing to say.
    /// </summary>
    public string? NotCompared { get; init; }

    /// <summary>Whether a check is in the lists: not hidden, and among the view's checks where there are some.</summary>
    public bool Lists(string checkId) =>
        !HiddenChecks.Contains(checkId, StringComparer.Ordinal) && (Checks is null || Checks.Contains(checkId, StringComparer.Ordinal));
}
