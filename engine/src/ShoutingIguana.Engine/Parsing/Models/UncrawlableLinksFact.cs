namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>
/// The page's link-like elements with no address search engines follow (an <c>&lt;a&gt;</c> with a <c>javascript:</c> or
/// empty href, one working only through onclick or a routerLink, another element given an href): how many, and the first
/// few.
/// </summary>
public sealed record UncrawlableLinksFact(int Count, IReadOnlyList<UncrawlableLinkFact> Examples);
