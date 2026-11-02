namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One URL of a <see cref="UrlPage"/>: its values in the order of <see cref="UrlPage.Columns"/>, each a long, a double, a
/// string (text, or an enum's name), a bool, or null when unknown.
/// </summary>
public sealed record UrlRow(int Id, IReadOnlyList<object?> Values);
