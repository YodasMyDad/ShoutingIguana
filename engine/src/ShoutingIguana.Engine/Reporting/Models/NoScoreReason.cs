namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>Why a crawl has no score: there is nothing honest to say.</summary>
internal enum NoScoreReason
{
    /// <summary>No page was read: a start URL that didn't answer, or that robots.txt refuses.</summary>
    NoPages
}
