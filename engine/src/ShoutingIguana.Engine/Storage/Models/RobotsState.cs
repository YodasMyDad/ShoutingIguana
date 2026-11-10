namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What an origin's answer for robots.txt meant to the crawl, as RFC 9309 has it.</summary>
public enum RobotsState
{
    /// <summary>A 2xx: its rules applied.</summary>
    Read,

    /// <summary>Another 4xx than 429, or redirects that couldn't be followed: no rules, so everything was allowed.</summary>
    Missing,

    /// <summary>A 5xx, a 429 or no answer: nothing could be read, which on the site itself refuses everything until it can.</summary>
    Unreachable
}
