namespace ShoutingIguana.Engine.Storage.Models;

public enum FetchMode : byte
{
    /// <summary>Fetch, parse and follow links: internal pages inside the start folder.</summary>
    Crawl = 0,

    /// <summary>Status check only: resources, external links, and internal URLs outside the start folder.</summary>
    Check = 1
}
