namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// Distinct URLs a page links to that share a problem, in the order the page links to them: the issue names every one
/// (<see cref="Models.IssueHit.Targets"/>), and the first becomes its related URL.
/// </summary>
internal sealed class TargetSet
{
    private readonly HashSet<int> _seen = [];
    private readonly List<int> _ids = [];

    public int Count => _ids.Count;

    public int? First => _ids.Count > 0 ? _ids[0] : null;

    /// <summary>Every target, the first first.</summary>
    public IReadOnlyList<int> Ids => _ids;

    public void Add(int id)
    {
        if (_seen.Add(id))
        {
            _ids.Add(id);
        }
    }
}
