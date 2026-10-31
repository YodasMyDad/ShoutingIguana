namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>A robots.txt group while it is being parsed: the user agents it names and their rules.</summary>
internal sealed class RobotsGroupBuilder
{
    public List<string> Agents { get; } = [];
    public List<RobotsRule> Rules { get; } = [];
    public double? CrawlDelaySeconds { get; set; }

    /// <summary>Once a group has rules, the next user-agent line starts a new group.</summary>
    public bool HasDirectives { get; set; }
}
