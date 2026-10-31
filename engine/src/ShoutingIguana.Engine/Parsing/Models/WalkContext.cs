namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>State inherited by an element's children during the document walk.</summary>
/// <param name="ExcludeText">Text here is not main content (head, nav, footer, noscript).</param>
/// <param name="InArticle">Inside main or article, where header and footer are section headings, not site chrome.</param>
internal readonly record struct WalkContext(LinkPosition Position, bool InHead, bool ExcludeText, bool InArticle);
