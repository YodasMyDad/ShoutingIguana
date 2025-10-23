namespace ShoutingIguana.Core.Models;

public class Header
{
    public int Id { get; set; }
    public int UrlId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    
    // Navigation properties
    public Url Url { get; set; } = null!;
}

