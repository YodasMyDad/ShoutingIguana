namespace ShoutingIguana.Plugins.CustomExtraction.Models;

/// <summary>
/// Represents a custom extraction rule.
/// </summary>
public class ExtractionRule
{
    public string Name { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public SelectorType SelectorType { get; set; }
    public string Selector { get; set; } = string.Empty;
}

/// <summary>
/// Type of selector used for extraction.
/// </summary>
public enum SelectorType
{
    Css,
    XPath,
    Regex
}

