namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One origin's robots.txt, line by line, with what each line blocked (<see cref="RobotsReader.ReadFile"/>).</summary>
/// <param name="UserAgent">The name the crawl looked for its group by.</param>
/// <param name="Lines">The text as stored, split at its line breaks; empty unless it was read.</param>
public sealed record RobotsFile(RobotsOrigin Origin, string UserAgent, IReadOnlyList<RobotsFileLine> Lines, RobotsUnmatched Unmatched)
{
    /// <summary>False for a crawl told to ignore robots.txt, which blocks nothing: as <see cref="RobotsOriginPage.Respected"/>.</summary>
    public bool Respected { get; init; } = true;
}
