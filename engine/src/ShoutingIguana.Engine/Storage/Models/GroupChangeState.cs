namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How one cause of a check moved since the other crawl (<see cref="GroupChange"/>), served in camelCase.</summary>
public enum GroupChangeState
{
    /// <summary>No such cause then.</summary>
    New,
    Fewer,
    More,
    Same
}
