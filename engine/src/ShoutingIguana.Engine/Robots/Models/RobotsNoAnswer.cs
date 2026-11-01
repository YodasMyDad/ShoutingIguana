using ShoutingIguana.Engine.Fetching.Models;

namespace ShoutingIguana.Engine.Robots.Models;

/// <summary>How robots.txt failed when its origin gave no answer at all, after its retries.</summary>
/// <param name="Outcome">Timeout or NetworkError.</param>
/// <param name="Error">The error of the last attempt.</param>
internal sealed record RobotsNoAnswer(FetchOutcome Outcome, string Error);
