using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>How the site answered a recheck's fetch of a URL (<see cref="RecheckAnswer"/>), from what the fetch stored.</summary>
internal static class RecheckAnswers
{
    public static RecheckAnswer Of(ResponseRow response)
    {
        var error = response.Error ?? string.Empty;
        return response.Outcome switch
        {
            // Asked, and refused: told to slow down, then given up on.
            FetchOutcome.NetworkError when error.StartsWith(RetryingFetcher.RATE_LIMITED_ERROR, StringComparison.Ordinal) => RecheckAnswer.RateLimited,

            // Failed without a request: the host had stopped answering, or the address is one a public site's crawl never asks.
            FetchOutcome.NetworkError or FetchOutcome.Timeout when error.StartsWith(RetryingFetcher.NOT_CHECKED_ERROR, StringComparison.Ordinal) => RecheckAnswer.NotAsked,
            FetchOutcome.NetworkError or FetchOutcome.Timeout => RecheckAnswer.NoAnswer,
            FetchOutcome.Blocked => RecheckAnswer.Blocked,
            FetchOutcome.RobotsDisallowed => RecheckAnswer.RobotsTxt,
            _ => RecheckAnswer.Read
        };
    }
}
