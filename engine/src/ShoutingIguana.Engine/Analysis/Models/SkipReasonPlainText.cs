namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Why a check couldn't run, in plain words (<see cref="PlainWords.SkipReason"/>).</summary>
/// <param name="Text">Why, in a sentence.</param>
/// <param name="Action">What would let the check run, in a sentence.</param>
public sealed record SkipReasonPlainText(string Text, string Action);
