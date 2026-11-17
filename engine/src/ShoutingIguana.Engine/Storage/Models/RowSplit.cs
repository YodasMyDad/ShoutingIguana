namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a check's rows moved between two crawls: how many are in each <see cref="RowChangeState"/>.</summary>
public sealed record RowSplit(long Fixed, long Gone, long NotChecked, long Still, long New, long NewUrl, long NotComparedThen)
{
    public static RowSplit None { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>The rows in one state.</summary>
    public long Count(RowChangeState state) => state switch
    {
        RowChangeState.Fixed => Fixed,
        RowChangeState.Gone => Gone,
        RowChangeState.NotChecked => NotChecked,
        RowChangeState.Still => Still,
        RowChangeState.New => New,
        RowChangeState.NewUrl => NewUrl,
        _ => NotComparedThen
    };

    /// <summary>The two splits added up.</summary>
    public RowSplit Plus(RowSplit other) => new(Fixed + other.Fixed, Gone + other.Gone, NotChecked + other.NotChecked, Still + other.Still,
        New + other.New, NewUrl + other.NewUrl, NotComparedThen + other.NotComparedThen);

    /// <summary>A split counted from each row's state.</summary>
    public static RowSplit Of(IEnumerable<RowChangeState> states)
    {
        var counts = new long[Enum.GetValues<RowChangeState>().Length];
        foreach (var state in states)
        {
            counts[(int)state]++;
        }

        return new RowSplit(counts[(int)RowChangeState.Fixed], counts[(int)RowChangeState.Gone], counts[(int)RowChangeState.NotChecked],
            counts[(int)RowChangeState.Still], counts[(int)RowChangeState.New], counts[(int)RowChangeState.NewUrl], counts[(int)RowChangeState.NotComparedThen]);
    }
}
