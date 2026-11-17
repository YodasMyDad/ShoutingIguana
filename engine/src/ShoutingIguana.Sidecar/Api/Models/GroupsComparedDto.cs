using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What a page of a check's groups was compared with (the fix loop).</summary>
/// <param name="With">The other crawl's id.</param>
/// <param name="FixedGroups">How many causes then have no URL now (what <c>/fixed</c> lists by cause); null for a check that isn't grouped or didn't run in both, and when counts only.</param>
internal sealed record GroupsComparedDto(string With, DateTime? StartedAt, Comparability Comparable, long? FixedGroups);
