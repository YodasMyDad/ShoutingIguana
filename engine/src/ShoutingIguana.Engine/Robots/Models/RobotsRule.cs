namespace ShoutingIguana.Engine.Robots.Models;

/// <param name="Pattern">Normalised path pattern without a trailing <c>$</c>.</param>
/// <param name="IsAnchored">The pattern ended with <c>$</c> and must match the whole path.</param>
/// <param name="Specificity">Length of the normalised rule, its <c>$</c> included; the longest matching rule wins.</param>
internal sealed record RobotsRule(string Pattern, bool Allow, bool IsAnchored, bool HasWildcard, int Specificity);
