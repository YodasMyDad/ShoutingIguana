namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// A check's fix as code: the offending tag, attribute, header or file line as it is now and as it should be, short
/// enough to read at a glance (one to four lines, elided content written as <c>…</c>). Only for a check whose fix is
/// an obvious, small piece of code or configuration: an editorial or structural fix keeps its prose alone
/// (<see cref="IssueDefinition.Fix"/>). The corrected code clears the check as its rule is written. Only how the check
/// is shown: it isn't stored with an analysis, nor part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
/// </summary>
public sealed record FixExample
{
    /// <summary>What the code is written in.</summary>
    public required FixLanguage Language { get; init; }

    /// <summary>The corrected code.</summary>
    public required string After { get; init; }

    /// <summary>
    /// The code as it is now, or null when the fix adds something that isn't there at all (a missing meta description).
    /// </summary>
    public string? Before { get; init; }

    /// <summary>
    /// Which case this is, as a sentence without its full stop ("The link wraps an icon or an inline SVG"), for a check
    /// with several; null when there is one and it needs no condition.
    /// </summary>
    public string? When { get; init; }

    /// <summary>
    /// Where the code goes when that isn't obvious, written to follow a label ("in the page's &lt;head&gt;", "in the
    /// server's response headers"); null when it is.
    /// </summary>
    public string? Where { get; init; }

    /// <summary>
    /// A short stable key for the case ("icon-link"), so a host can show first the case a crawl's evidence points to;
    /// null for a check with one example.
    /// </summary>
    public string? Case { get; init; }
}
