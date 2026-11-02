using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A link of the URL inspected, naming the other end, with its flags as names.</summary>
internal sealed record LinkRowDto(
    int Id,
    string Url,
    LinkKind? Kind,
    string? Text,
    LinkPosition? Position,
    IReadOnlyList<string> Flags,
    bool? Followed,
    string? Fragment,
    int? StatusCode,
    bool? Indexable,
    bool Fetched)
{
    public static LinkRowDto From(LinkRow link) => new(
        link.Id, link.Url, link.Kind, link.Text, link.Position,
        [.. Enum.GetValues<LinkFlags>().Where(flag => flag != LinkFlags.None && link.Flags.HasFlag(flag)).Select(flag => char.ToLowerInvariant(flag.ToString()[0]) + flag.ToString()[1..])],
        link.Followed, link.Fragment, link.StatusCode, link.Indexable, link.Fetched);
}
