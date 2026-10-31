namespace ShoutingIguana.Engine.Fetching.Models;

public enum FetchOutcome
{
    Success,
    Redirect,
    ClientError,
    ServerError,

    /// <summary>A firewall, bot challenge or rate limit stopped the request.</summary>
    Blocked,
    RobotsDisallowed,
    Timeout,
    NetworkError,

    /// <summary>The response arrived but the crawler failed to process it. Logged with the exception.</summary>
    ProcessingError
}
