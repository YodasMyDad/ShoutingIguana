using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Models;

/// <summary>
/// Represents a finding discovered by a plugin during URL analysis.
/// </summary>
public class Finding
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int UrlId { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime CreatedUtc { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    public Url Url { get; set; } = null!;
}

