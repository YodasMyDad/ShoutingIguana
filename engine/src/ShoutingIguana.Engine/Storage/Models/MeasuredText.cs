namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Text as the checks measure it.</summary>
/// <param name="Length">User-perceived characters, after runs of white space become one space.</param>
/// <param name="Width">
/// For a title or meta description, its width in Google's desktop results in pixels, which the checks judge
/// (<see cref="Analysis.Rules.SearchResultWidth"/>); null for an H1, which no result shows.
/// </param>
public sealed record MeasuredText(string Text, int Length, int? Width = null);
