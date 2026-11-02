namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a parsed page says, as the checks judge it.</summary>
/// <param name="Titles">Every title element, in page order, with the room each takes.</param>
/// <param name="Headings">How many H1 to H6 headings, in that order.</param>
/// <param name="Robots">The robots directives that apply to the crawler and to Googlebot, from meta tags and headers.</param>
public sealed record PageContent(
    IReadOnlyList<MeasuredText> Titles,
    IReadOnlyList<MeasuredText> Descriptions,
    IReadOnlyList<MeasuredText> H1s,
    IReadOnlyList<int> Headings,
    int WordCount,
    string? Language,
    string? Charset,
    IReadOnlyList<string> Robots,
    MetaRefreshInfo? MetaRefresh,
    int Images,
    int ImagesMissingAlt);
