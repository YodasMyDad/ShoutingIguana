namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Whether a grid reads the URL summary, or only the crawl's own tables, there being no summary that describes it.</summary>
internal enum GridMode
{
    Summary,
    Crawl
}
