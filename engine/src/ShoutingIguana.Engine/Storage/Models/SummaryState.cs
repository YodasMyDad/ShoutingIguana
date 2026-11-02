namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Whether the URL summary describes the crawl, so the columns that need it have values, and if not, why not.</summary>
public sealed record SummaryState(bool Available, SummaryUnavailableReason? Reason)
{
    public static SummaryState Present { get; } = new(true, null);
}
