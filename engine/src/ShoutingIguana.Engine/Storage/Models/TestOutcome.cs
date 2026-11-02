namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>How a URL did on one test of its indexability.</summary>
public enum TestOutcome
{
    Pass,
    Fail,

    /// <summary>The crawl never read what decides it: a page only status-checked, robots.txt ignored, or a test made moot.</summary>
    NotChecked
}
