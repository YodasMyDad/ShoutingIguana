namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>
/// The targets of a filter set's links in the order of a sort by the target, each with the matching links up to and
/// including its own: a page anywhere in that sort starts at the first target whose count passes the page's offset.
/// </summary>
/// <param name="Targets">Target URL ids, in sort order.</param>
/// <param name="Through">For each target, the matching links to it and to every target before it.</param>
internal sealed record LinkTargetOrder(int[] Targets, long[] Through);
