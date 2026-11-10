namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>An origin's blocked URLs that no line of its stored robots.txt accounts for.</summary>
/// <param name="Unreachable">robots.txt couldn't be read when the URL came up, which refuses everything.</param>
/// <param name="Unknown">The file kept doesn't refuse the URL: a later run read it again, and it had changed.</param>
public sealed record RobotsUnmatched(long Unreachable, long Unknown);
