namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>What a score line's count is a share of.</summary>
public enum ScoreUnit
{
    /// <summary>The pages the analysis read: the check flags pages only (<see cref="IssueSubject.Page"/>).</summary>
    Pages,

    /// <summary>The URLs crawled: the check can flag any URL (<see cref="IssueSubject.Url"/>).</summary>
    Urls,

    /// <summary>Nothing: a site-wide fault counts as the whole site, so its count and total aren't for showing as a share.</summary>
    Site
}
