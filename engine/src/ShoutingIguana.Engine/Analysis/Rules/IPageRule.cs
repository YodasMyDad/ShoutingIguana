using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>A check that needs only one page: its facts, headers and outgoing links (and the status of their targets).</summary>
internal interface IPageRule
{
    IEnumerable<IssueHit> Check(AnalyzedPage page);
}
