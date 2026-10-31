namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Distinct URLs a page links to that share a problem; the first becomes the issue's related URL.</summary>
internal sealed class TargetSet
{
    private readonly HashSet<int> _ids = [];

    public int Count => _ids.Count;

    public int? First { get; private set; }

    public void Add(int id)
    {
        if (_ids.Add(id))
        {
            First ??= id;
        }
    }
}
