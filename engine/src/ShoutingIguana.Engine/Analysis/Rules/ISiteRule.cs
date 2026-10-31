using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>A check across pages: it sees every page during the page pass, then reports once all pages are known.</summary>
internal interface ISiteRule
{
    void Observe(AnalyzedPage page);

    IEnumerable<IssueHit> Finish();
}
