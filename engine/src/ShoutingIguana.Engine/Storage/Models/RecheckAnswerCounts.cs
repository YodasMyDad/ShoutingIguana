namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// How the site answered a recheck for the URLs it judges (<see cref="RecheckAnswer"/>), with or without a check: they add
/// up to the URLs it was asked for.
/// </summary>
/// <param name="Read">Read: the site answered.</param>
/// <param name="RateLimited">Refused again: told to slow down (429).</param>
/// <param name="Blocked">Refused again: a firewall or bot check answered.</param>
/// <param name="RobotsTxt">Refused again: robots.txt disallows them, or couldn't be read.</param>
/// <param name="NoAnswer">Refused again: no answer.</param>
/// <param name="NotAsked">Never asked: past the cap, or the run ended before them (stopped, its time ran out, the host had stopped answering).</param>
public sealed record RecheckAnswerCounts(int Read, int RateLimited, int Blocked, int RobotsTxt, int NoAnswer, int NotAsked)
{
    /// <summary>Asked, and refused again, for any reason.</summary>
    public int Refused => RateLimited + Blocked + RobotsTxt + NoAnswer;
}
