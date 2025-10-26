namespace ShoutingIguana.ViewModels.Models;

public class LinkDisplayModel
{
    public int Id { get; set; }
    public string FromUrl { get; set; } = string.Empty;
    public string ToUrl { get; set; } = string.Empty;
    public string? AnchorText { get; set; }
    public string LinkType { get; set; } = string.Empty;
}

