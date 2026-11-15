namespace ShoutingIguana.Engine.Export.Models;

/// <summary>
/// Columns a grid's file adds after its own, read for each page of rows at once rather than from the grid: where each of
/// a check's URLs is found, or what each of its pages names.
/// </summary>
/// <param name="Titles">The added columns' headers.</param>
/// <param name="Read">The added cells of each row on a page, by URL id: a cell for each title. A row it leaves out has none.</param>
internal sealed record ExportExtraColumns(IReadOnlyList<string> Titles, Func<IReadOnlyList<int>, IReadOnlyDictionary<int, object?[]>> Read);
