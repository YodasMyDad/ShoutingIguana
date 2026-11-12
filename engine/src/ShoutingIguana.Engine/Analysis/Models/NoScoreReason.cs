namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Why an analysis has no score. A crawl that was never analysed has no analysis to ask.</summary>
public enum NoScoreReason
{
    /// <summary>The analysis read no page, so there is nothing to score.</summary>
    NoPages,

    /// <summary>
    /// The analysis stored no score this engine can read: an engine from before the score ran it, or one whose score this
    /// engine doesn't understand. The analysis is out of date, and analysing again scores it.
    /// </summary>
    NotScored
}
