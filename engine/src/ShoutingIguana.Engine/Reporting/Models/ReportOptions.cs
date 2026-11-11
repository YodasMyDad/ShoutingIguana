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
}
