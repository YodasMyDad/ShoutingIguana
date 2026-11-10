namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>One line of a robots.txt file as the parser read it, for showing the file beside what it blocked.</summary>
/// <param name="Number">Its place in the file, from 1.</param>
/// <param name="Text">The line as written, without its line break.</param>
internal sealed record RobotsLine(int Number, string Text, RobotsLineKind Kind)
{
    /// <summary>
    /// Whether the line is part of what the crawler obeyed: a line of a group that applies to its user agent, or a sitemap
    /// line, which applies to everyone. Null for a line that's neither.
    /// </summary>
    public bool? Applies { get; set; }

    public RobotsLineProblem? Problem { get; init; }
}
