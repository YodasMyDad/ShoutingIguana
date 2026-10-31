using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A discovered link, normalised and scoped by the worker so the writer only does lookups.</summary>
/// <param name="IsCrawlable">Internal, inside the start folder, and a page-type link: fetch and parse it.</param>
internal sealed record OutLink(
    string Url,
    UInt128 Key,
    bool IsInternal,
    bool IsCrawlable,
    LinkKind Kind,
    LinkFlags Flags,
    LinkPosition Position,
    string? Text,
    string? Fragment);
