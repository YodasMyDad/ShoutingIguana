namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a custom extraction rule for extracting data from crawled pages.
/// </summary>
public class CustomExtractionRule
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of selector: 0=CSS, 1=XPath, 2=Regex
    /// </summary>
    public int SelectorType { get; set; }
    
    public string Selector { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    
    // Navigation properties
    public Project? Project { get; set; }
}

