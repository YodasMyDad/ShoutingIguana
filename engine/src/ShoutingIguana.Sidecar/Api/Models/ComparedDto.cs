using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>What a page of a check's rows, or of what it no longer flags, was compared with (the fix loop).</summary>
/// <param name="With">The other crawl's id.</param>
internal sealed record ComparedDto(string With, DateTime? StartedAt, Comparability Comparable);
