using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>A check across pages: it sees every page during the page pass, then reports once all pages are known.</summary>
internal interface ISiteRule
{
    /// <summary>
    /// The checks <see cref="Finish"/> reports, and only those, which no page rule reports. When it fails they have no
    /// results at all, which isn't the same as finding nothing, so the analysis skips them saying why
    /// (<see cref="SkippedCheckReasons.RULE_FAILED"/>); a result for any other check is the rule's own bug, and counts as
    /// its failure.
    /// </summary>
    IReadOnlyList<IssueDefinition> Checks { get; }

    void Observe(AnalyzedPage page);

    IEnumerable<IssueHit> Finish();
}
