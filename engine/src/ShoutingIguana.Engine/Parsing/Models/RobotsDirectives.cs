namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>Combined robots meta tags and X-Robots-Tag headers that apply to the crawler and to Googlebot.</summary>
/// <param name="Directives">Every directive found, lowercase, e.g. <c>noindex</c> or <c>max-snippet:50</c>.</param>
public sealed record RobotsDirectives(bool NoIndex, bool NoFollow, IReadOnlyList<string> Directives)
{
    public static RobotsDirectives None { get; } = new(false, false, []);
}
