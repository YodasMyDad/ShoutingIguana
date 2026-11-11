namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Why a score covers only part of the site: the crawl it was worked out from didn't read all of it.</summary>
public enum ScoreIncompleteReason
{
    /// <summary>The crawl was stopped, or stopped itself, before it finished. It can be continued.</summary>
    Stopped,

    /// <summary>The crawl failed. What it read is kept.</summary>
    Failed,

    /// <summary>The crawl finished, but its URL limit kept internal URLs out.</summary>
    UrlLimit,

    /// <summary>The crawl finished, but its depth limit kept internal URLs out.</summary>
    DepthLimit
}
