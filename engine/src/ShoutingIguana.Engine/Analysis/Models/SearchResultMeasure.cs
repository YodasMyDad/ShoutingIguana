namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Text measured as a search result shows it (<see cref="Rules.SearchResultWidth.Measure"/>).</summary>
/// <param name="Characters">User-perceived characters, after runs of white space become one space and the ends are trimmed.</param>
/// <param name="Pixels">The width in Google's desktop results, in whole pixels.</param>
internal readonly record struct SearchResultMeasure(int Characters, int Pixels);
