namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>What a check's count is a share of.</summary>
internal enum ScoreUnit
{
    /// <summary>The pages read: the check flags pages only.</summary>
    Pages,

    /// <summary>The addresses checked: the check can flag any URL.</summary>
    Urls,

    /// <summary>Nothing: the fault is the whole site's, whatever its count.</summary>
    Site
}
