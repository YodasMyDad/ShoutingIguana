namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>How a custom extraction rule finds its text on a page.</summary>
public enum ExtractionSelector
{
    /// <summary>A CSS selector. Each matching element's text is a value.</summary>
    Css = 0,

    /// <summary>An XPath expression. Each matching node's text is a value.</summary>
    XPath = 1,

    /// <summary>
    /// A regular expression run on the page's HTML. The first capture group's text is the value, or the whole match
    /// when the pattern has no capture group.
    /// </summary>
    Regex = 2
}
