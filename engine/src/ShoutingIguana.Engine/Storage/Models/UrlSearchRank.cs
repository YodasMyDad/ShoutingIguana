namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How well a URL matches a search's text, best first (<see cref="UrlSearchReader.Search"/>).</summary>
public enum UrlSearchRank
{
    /// <summary>
    /// The text is the URL: the whole of it, with or without its scheme, or its path (with its query string, if it has
    /// one), with or without the slashes around it.
    /// </summary>
    Exact,

    /// <summary>
    /// The text starts a segment of the URL's path or its host name (with or without its <c>www.</c>), or starts a word
    /// of the title.
    /// </summary>
    Start,

    /// <summary>The text is somewhere else in the URL or the title.</summary>
    Anywhere
}
