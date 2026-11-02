namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// What became of a request, as a grid shows it: the stored outcome, or <see cref="NotChecked"/> for a URL the crawler gave
/// up on without asking, its host having stopped answering, which the analysis counts as never fetched.
/// </summary>
public enum UrlOutcome
{
    Success,
    Redirect,
    ClientError,
    ServerError,
    Blocked,
    RobotsDisallowed,
    Timeout,
    NetworkError,
    ProcessingError,
    NotChecked
}
