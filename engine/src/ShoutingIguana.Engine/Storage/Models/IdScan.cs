namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>One table read for a condition's URL ids: the rows of <see cref="From"/> that match <see cref="Condition"/>, each giving <see cref="Id"/>.</summary>
/// <param name="From">The table and its alias, such as <c>responses r</c>.</param>
/// <param name="Combinable">
/// The condition reads every row of the table, so several conditions on it share one scan; otherwise (a check's issues,
/// found through their key) it reads its own rows.
/// </param>
internal sealed record IdScan(string From, string Id, string Condition, bool Combinable);
