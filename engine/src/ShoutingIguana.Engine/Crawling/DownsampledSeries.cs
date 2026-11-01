namespace ShoutingIguana.Engine.Crawling;

/// <summary>
/// A series that keeps at most <c>capacity</c> of the points added to it, evenly spread over all of them, however many
/// come: when it is full, every other point kept is dropped, and from then on one in two points added is kept, then one
/// in four, and so on. The first point and the latest are always there. Memory stays flat, which is what a crawl's
/// progress over time needs: one point every half second for as long as it runs. Not thread-safe.
/// </summary>
/// <param name="capacity">An even number, at least 2: halving it keeps the spacing even.</param>
public sealed class DownsampledSeries<T>(int capacity)
{
    private readonly int _capacity = capacity >= 2 && capacity % 2 == 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be an even number, at least 2.");

    private readonly List<T> _kept = new(capacity);
    private long _added;
    private long _stride = 1;
    private T? _latest;

    /// <summary>How many points were added, kept or not.</summary>
    public long Added => _added;

    /// <summary>One point is kept for every this many added.</summary>
    public long Stride => _stride;

    public void Add(T point)
    {
        _latest = point;
        var index = _added++;
        if (index % _stride != 0)
        {
            return;
        }

        if (_kept.Count == _capacity)
        {
            // Every other one goes. This point's index is a multiple of the doubled stride too, as the capacity is even.
            for (var kept = 0; kept < _capacity / 2; kept++)
            {
                _kept[kept] = _kept[kept * 2];
            }

            _kept.RemoveRange(_capacity / 2, _capacity / 2);
            _stride *= 2;
        }

        _kept.Add(point);
    }

    /// <summary>The points kept, oldest first, and the latest added after them when it wasn't one: at most capacity plus one.</summary>
    public IReadOnlyList<T> Points()
    {
        if (_added == 0)
        {
            return [];
        }

        List<T> points = [.. _kept];
        if ((_added - 1) % _stride != 0)
        {
            points.Add(_latest!);
        }

        return points;
    }
}
