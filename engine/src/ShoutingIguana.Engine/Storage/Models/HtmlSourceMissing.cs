namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Why a URL has no stored HTML.</summary>
public enum HtmlSourceMissing
{
    /// <summary>The crawl didn't store HTML: it's off unless asked for.</summary>
    NotStored,

    /// <summary>The crawl stored HTML, but this URL has none: it isn't a page fetched successfully.</summary>
    NotPage
}
