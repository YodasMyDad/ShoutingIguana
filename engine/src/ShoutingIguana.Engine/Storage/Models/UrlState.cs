namespace ShoutingIguana.Engine.Storage.Models;

public enum UrlState : byte
{
    Queued = 0,
    Fetching = 1,
    Done = 2,
    Skipped = 3
}
