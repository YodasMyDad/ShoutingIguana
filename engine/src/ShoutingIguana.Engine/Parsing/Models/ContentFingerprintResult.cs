namespace ShoutingIguana.Engine.Parsing.Models;

/// <param name="ContentHash">Exact-duplicate key over the normalised main-content words.</param>
/// <param name="SimHash">64-bit SimHash over 3-word shingles; a small Hamming distance means near-duplicate.</param>
public readonly record struct ContentFingerprintResult(int WordCount, ulong ContentHash, ulong SimHash);
