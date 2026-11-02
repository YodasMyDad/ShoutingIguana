namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Text with the room it takes in search results, as the checks measure it: full-width characters and emoji count two.</summary>
public sealed record MeasuredText(string Text, int Length);
