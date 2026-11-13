namespace ShoutingIguana.Engine.Export.Models;

/// <summary>A failure in plain words (<see cref="FailureWords.Of"/>).</summary>
/// <param name="Words">The words, as a clause: "404 Not Found", "it timed out".</param>
/// <param name="IsOther">Nothing more is known than that it failed: its words are "the request failed".</param>
internal readonly record struct PlainFailure(string Words, bool IsOther);
