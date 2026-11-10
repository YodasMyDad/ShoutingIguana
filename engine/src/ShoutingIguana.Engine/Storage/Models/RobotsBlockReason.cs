namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why robots.txt kept the crawl from a URL.</summary>
public enum RobotsBlockReason
{
    /// <summary>A rule of the stored file refuses it.</summary>
    Rule,

    /// <summary>robots.txt couldn't be read, which refuses everything.</summary>
    Unreachable,

    /// <summary>The stored file doesn't refuse it: it was read again later, and had changed.</summary>
    Unknown
}
