using ShoutingIguana.Core.Models;

namespace ShoutingIguana.Core.Services;

public interface ILinkExtractor
{
    Task<IEnumerable<ExtractedLink>> ExtractLinksAsync(string htmlContent, string baseUrl);
}

public class ExtractedLink
{
    public string Url { get; set; } = string.Empty;
    public string? AnchorText { get; set; }
    public LinkType LinkType { get; set; }
    public string? RelAttribute { get; set; }
}

