namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// When a page is fetched again in a headless browser and that HTML is what gets parsed. The first fetch is always
/// plain HTTP. Off never renders. Automatic renders a page whose HTML is a client-rendered shell and leaves every
/// other page as the HTTP response. JavaScript renders every HTML page, and still keeps a link that was only in the
/// raw HTML, as Screaming Frog does when Rendering is JavaScript. Matching renders only the URLs
/// <see cref="CrawlOptions.RenderPatterns"/> name.
/// </summary>
public enum JavaScriptRendering
{
    /// <summary>Never render. A crawl file from before rendering was stored reads as this: the property is absent.</summary>
    Off = 0,

    /// <summary>Render a page whose raw HTML is a client-rendered shell. The default of a new crawl.</summary>
    Automatic = 1,

    /// <summary>Render only URLs that match <see cref="CrawlOptions.RenderPatterns"/>, whatever their HTML says.</summary>
    Matching = 2,

    /// <summary>
    /// Render every HTML page. The page that is stored is the rendered HTML, and a link that exists only in the raw
    /// HTML is still followed.
    /// </summary>
    JavaScript = 3
}
