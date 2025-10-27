namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Represents a recently opened project.
/// </summary>
public class RecentProject
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastOpenedUtc { get; set; }
}

