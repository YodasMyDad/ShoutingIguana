namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One page of the targets of the links a view matches, each once with its links, the pages they're on and what they say
/// (<see cref="LinkGridReader.Groups"/>), in the order asked for.
/// </summary>
/// <param name="Total">How many targets there are: <see cref="LinkGridReader.Ends"/>'s total for the same links.</param>
/// <param name="Links">The links the view matches: the links grid's <c>matching</c>.</param>
/// <param name="Pages">The pages the view's links are on, each once.</param>
/// <param name="Sort">The order the targets are in, as asked or by default.</param>
public sealed record LinkGroupPage(long Total, long Links, long Pages, int Offset, UrlSort Sort, IReadOnlyList<LinkGroupRow> Rows, SummaryState Summary)
{
    /// <summary>The sort by how many pages link to each target.</summary>
    public const string PAGES = "pages";

    /// <summary>The sort by how many of the view's links lead to each target.</summary>
    public const string LINKS = "links";

    public const int DEFAULT_LIMIT = 100;

    /// <summary>The most targets a page holds: each reads its links' text.</summary>
    public const int MAX_LIMIT = 200;

    /// <summary>The most of a target's links read for their text: <see cref="LinkGroupRow.Text"/> is the most common among them.</summary>
    public const int TEXT_SAMPLE = 200;
}
