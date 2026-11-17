namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>Which line of a search result text is measured as: each has its own font size (<see cref="Rules.SearchResultWidth"/>).</summary>
internal enum SearchResultText
{
    /// <summary>The title link: Arial at 20 px.</summary>
    Title,

    /// <summary>The snippet, from the meta description: Arial at 14 px.</summary>
    Description
}
