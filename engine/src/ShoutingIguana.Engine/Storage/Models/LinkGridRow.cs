namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// One link of a <see cref="LinkGridPage"/>: its id, and its values in the order of <see cref="LinkGridPage.Columns"/>,
/// each a long, a string (text, or an enum's name), a bool, or null when unknown.
/// </summary>
public sealed record LinkGridRow(long Id, IReadOnlyList<object?> Values);
