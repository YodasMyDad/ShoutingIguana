namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// The file a check of the whole site is about. Such a check is reported once, on the home page (<see cref="IssueDefinition.ReportedOnce"/>),
/// and stores the files it found at fault (<c>issue_files</c>), so a view can name the file instead of the page. Only how a
/// check is shown: it isn't part of <see cref="AnalysisFreshness.ChecksFingerprint"/>.
/// </summary>
public enum IssueAbout
{
    /// <summary>The robots.txt of one of the site's origins.</summary>
    RobotsTxt,

    /// <summary>One of the site's sitemaps, or the conventional /sitemap.xml where none was found.</summary>
    Sitemap
}
