namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One end of a link: the page it's on, or the URL it leads to.</summary>
public enum LinkEnd
{
    /// <summary>The page the link is on: its source.</summary>
    From,

    /// <summary>The URL the link leads to: its target.</summary>
    To
}
