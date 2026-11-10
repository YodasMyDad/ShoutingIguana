namespace ShoutingIguana.Engine.Tests.Support;

/// <summary>What the grid readers' tests ask of a walk through every page of a read, by offset or by cursor.</summary>
internal static class GridWalks
{
    /// <summary>
    /// A page that says more follow has rows of its own, and leaves rows still to come: a cursor that led back to rows
    /// already read, or on to none, would walk for ever, and a test that walks it would hang where it should fail.
    /// </summary>
    /// <param name="rows">The rows of the page just read.</param>
    /// <param name="read">The rows read so far, this page's included.</param>
    /// <param name="matching">The rows the read says there are.</param>
    public static void AssertGoesOn(int rows, int read, long matching) =>
        Assert.True(rows > 0 && read < matching, $"A page of {rows} rows says more follow, after {read} of {matching}: the walk would never end.");
}
