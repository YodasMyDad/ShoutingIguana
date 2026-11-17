namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>How a host answered, fed back into its gate to learn the pace it can take.</summary>
internal enum HostSignal
{
    Success,

    /// <summary>429, or 503 with Retry-After: the host asked the crawler to slow down.</summary>
    Throttled,

    /// <summary>502, 504, and 503 without Retry-After: the host answered that it is struggling.</summary>
    Failed,

    /// <summary>
    /// No answer at all (a timeout, a dropped connection): whether the origin is down is judged apart, per origin, so a
    /// dead www twin never slows the site that answers.
    /// </summary>
    NoAnswer,

    /// <summary>A firewall or bot protection's block page: it says who the crawler is, not how fast it goes.</summary>
    Refused
}
