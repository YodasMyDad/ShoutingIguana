namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Union-find over 0..count-1 with path halving and union by size.</summary>
internal sealed class DisjointSets
{
    private readonly int[] _parent;
    private readonly int[] _size;

    public DisjointSets(int count)
    {
        _parent = new int[count];
        _size = new int[count];
        for (var index = 0; index < count; index++)
        {
            _parent[index] = index;
            _size[index] = 1;
        }
    }

    public int Find(int index)
    {
        while (_parent[index] != index)
        {
            _parent[index] = _parent[_parent[index]];
            index = _parent[index];
        }

        return index;
    }

    public void Union(int first, int second)
    {
        var (a, b) = (Find(first), Find(second));
        if (a == b)
        {
            return;
        }

        if (_size[a] < _size[b])
        {
            (a, b) = (b, a);
        }

        _parent[b] = a;
        _size[a] += _size[b];
    }

    /// <summary>Members in the group whose root is <paramref name="root"/>.</summary>
    public int Size(int root) => _size[root];
}
