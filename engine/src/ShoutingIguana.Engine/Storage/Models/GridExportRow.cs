namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One row of a <see cref="GridExportPage"/>: a URL's id or a link's, and its values in the order of the page's columns.</summary>
internal readonly record struct GridExportRow(long Id, IReadOnlyList<object?> Values);
