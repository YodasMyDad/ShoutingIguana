namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>How much of the report a document holds.</summary>
public enum ReportCut
{
    /// <summary>The whole report.</summary>
    Full,

    /// <summary>
    /// The report's first page: the head, the summary, what to fix first, how the score is worked out, and the foot. It is
    /// the full report's own opening, so the two can't differ.
    /// </summary>
    Summary
}
