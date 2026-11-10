using ShoutingIguana.Engine.Robots.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One line of a stored robots.txt.</summary>
/// <param name="Number">From 1.</param>
/// <param name="Applies">Whether it's part of the group the crawl obeyed; a sitemap line applies to everyone. Null for other kinds.</param>
/// <param name="Blocked">On a disallow line that applies: how many of the origin's blocked URLs it refused. Null on every other line.</param>
/// <param name="Problem">Why the crawler made nothing of the line, or null.</param>
public sealed record RobotsFileLine(int Number, string Text, RobotsLineKind Kind, bool? Applies, long? Blocked, RobotsLineProblem? Problem);
