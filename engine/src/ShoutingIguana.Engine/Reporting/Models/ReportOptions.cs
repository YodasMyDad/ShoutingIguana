namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>How the report is cut, and how many of each problem's pages it lists; the rest are in the workbook.</summary>
public sealed record ReportOptions
{
    public const int DEFAULT_EXAMPLES = 5;

    public const int DEFAULT_LOOK_EXAMPLES = 3;

    /// <summary>The most examples a problem can be asked to list: the report explains, and the workbook lists.</summary>
    public const int MAX_EXAMPLES = 50;

    public ReportCut Cut { get; init; } = ReportCut.Full;

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

    /// <summary>Whether a check is in the lists: not hidden, and among the view's checks where there are some.</summary>
    public bool Lists(string checkId) =>
        !HiddenChecks.Contains(checkId, StringComparer.Ordinal) && (Checks is null || Checks.Contains(checkId, StringComparer.Ordinal));
}
