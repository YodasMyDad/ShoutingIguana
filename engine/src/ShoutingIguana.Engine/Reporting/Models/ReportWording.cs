namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// Whose words the report's rows are in. The document is one outline for both readers; what differs is how each row
/// says what was found, and where each check's technical name sits.
/// </summary>
public enum ReportWording
{
    /// <summary>
    /// A site owner's, as Simple's screens say them: "The link that says “Buy Now”, in the main content", "It is missing
    /// (404 Not Found)". The checks' technical names are listed once, under About this check.
    /// </summary>
    Plain,

    /// <summary>
    /// A working SEO's, as Advanced says them: "404 Not Found", "content, “Buy Now”", "Inlinks: 1". Each problem carries
    /// its check's technical name and severity beside its title.
    /// </summary>
    Technical
}
