namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>One link-like element search engines can't follow.</summary>
/// <param name="Text">What it says, as a link's label is read, cut short; empty with nothing to read.</param>
/// <param name="Why">What makes it uncrawlable: <c>javascript:</c>, <c>empty href</c>, <c>onclick</c>, <c>routerLink</c> or <c>&lt;span href&gt;</c>.</param>
public sealed record UncrawlableLinkFact(string Text, string Why);
