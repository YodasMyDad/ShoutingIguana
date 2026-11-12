namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A URL at one end of the links a view matches.</summary>
/// <param name="Links">How many of the view's links have it at that end.</param>
public sealed record LinkEndRow(int Id, string Url, long Links);
