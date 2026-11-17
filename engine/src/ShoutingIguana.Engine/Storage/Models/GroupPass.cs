namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A check's causes matched across two crawls.</summary>
/// <param name="ThenByKey">Every cause then by its detail and related URL's text, with its URLs then: how a cause now finds its counterpart.</param>
/// <param name="ClusterThen">
/// For a duplicate check, each cluster now (by its first page's id now) with the URLs of the cluster then it shares the
/// most pages with; a cluster now that shares none isn't in it.
/// </param>
/// <param name="Cleared">The causes then with no URL now, largest first.</param>
internal sealed record GroupPass(
    IReadOnlyDictionary<(string? Detail, string? RelatedUrl), long> ThenByKey,
    IReadOnlyDictionary<int, long> ClusterThen,
    IReadOnlyList<ThenGroup> Cleared);
