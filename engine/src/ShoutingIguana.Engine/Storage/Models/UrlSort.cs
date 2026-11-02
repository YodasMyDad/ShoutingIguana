namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One key of the URL grid's sort. Unknown values come last, whichever the direction.</summary>
public sealed record UrlSort(string Column, bool Descending = false);
