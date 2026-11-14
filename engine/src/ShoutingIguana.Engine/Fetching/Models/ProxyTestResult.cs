namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>
/// One fetch through a proxy: what happened, the HTTP status when one came back, how long it took, and a sentence that
/// says it in plain words, for the UI to show as it is.
/// </summary>
public sealed record ProxyTestResult(ProxyTestOutcome Outcome, int? Status, int ElapsedMs, string Message);
