namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Where a recheck's run got to (<c>rechecks.status</c>). Stored as its number.</summary>
public enum RecheckStatus
{
    /// <summary>Fetching now, in this process; or, read from a file nothing has open, cut short when its process ended.</summary>
    Running = 0,
    Completed = 1,
    Stopped = 2,
    Failed = 3,

    /// <summary>Its run ended with its process: the analysis after it settled what it had fetched.</summary>
    Interrupted = 4
}
