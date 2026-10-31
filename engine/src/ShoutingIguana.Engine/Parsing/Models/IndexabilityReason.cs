namespace ShoutingIguana.Engine.Parsing.Models;

public enum IndexabilityReason : byte
{
    None,
    Noindex,
    Canonicalised,
    Redirected,
    ClientError,
    ServerError,
    BlockedByRobots,
    Blocked,
    Unreachable
}
