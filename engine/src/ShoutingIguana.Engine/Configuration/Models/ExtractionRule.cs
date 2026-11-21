namespace ShoutingIguana.Engine.Configuration.Models;

/// <summary>
/// One custom extraction rule: a name, and a CSS selector, an XPath expression or a regular expression. The crawl
/// applies the enabled rules while it parses each HTML page, once, and stores what they match. A crawl file from
/// before this has none.
/// </summary>
public sealed record ExtractionRule
{
    /// <summary>At most this many rules on a crawl. Each one runs on every HTML page.</summary>
    public const int MAX_RULES = 40;

    /// <summary>How long a rule's name may be.</summary>
    public const int MAX_NAME_LENGTH = 80;

    /// <summary>How long a selector or pattern may be.</summary>
    public const int MAX_SELECTOR_LENGTH = 2_000;

    /// <summary>The name the extracted values are listed under. Unique among the crawl's rules.</summary>
    public string Name { get; init; } = "";

    public ExtractionSelector SelectorType { get; init; }

    /// <summary>The CSS selector, XPath expression or regular expression.</summary>
    public string Selector { get; init; } = "";

    /// <summary>A rule that is off is kept with the crawl and not applied.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The name and selector with surrounding space removed, which is what is stored and applied.</summary>
    public ExtractionRule Trimmed() => this with { Name = Name.Trim(), Selector = Selector.Trim() };

    /// <summary>
    /// <c>Name=css:selector</c>, <c>Name=xpath:expression</c> or <c>Name=regex:pattern</c>. The selector is everything
    /// after the type, so it may contain <c>:</c> and <c>=</c>.
    /// </summary>
    public static bool TryParse(string text, out ExtractionRule rule, out string error)
    {
        rule = new ExtractionRule();
        var split = text.IndexOf('=');
        if (split <= 0 || !Kind(text[(split + 1)..], out var type, out var selector))
        {
            error = $"'{text}' is not an extraction rule. Write Name=css:selector, Name=xpath:expression or Name=regex:pattern.";
            return false;
        }

        rule = new ExtractionRule { Name = text[..split].Trim(), SelectorType = type, Selector = selector.Trim() };
        error = "";
        return true;
    }

    private static bool Kind(string rest, out ExtractionSelector type, out string selector)
    {
        if (rest.StartsWith("css:", StringComparison.OrdinalIgnoreCase))
        {
            type = ExtractionSelector.Css;
            selector = rest["css:".Length..];
            return true;
        }

        if (rest.StartsWith("xpath:", StringComparison.OrdinalIgnoreCase))
        {
            type = ExtractionSelector.XPath;
            selector = rest["xpath:".Length..];
            return true;
        }

        if (rest.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            type = ExtractionSelector.Regex;
            selector = rest["regex:".Length..];
            return true;
        }

        type = default;
        selector = "";
        return false;
    }
}
