namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// A check's words as the report prints them. The plain words lead where the check has them; where it has none yet, the
/// check's technical texts stand in (<see cref="IsPlain"/> says which), and the technical title is printed beside either.
/// </summary>
/// <param name="Title">The problem as its owner would say it.</param>
/// <param name="What">What's wrong and why it matters, a sentence or a paragraph each.</param>
/// <param name="Fix">How to fix it, a step each.</param>
/// <param name="Fixer">Who usually does it; null when the check doesn't say.</param>
/// <param name="Passed">The good news when the check finds nothing, as a fact: "Every page has a title".</param>
/// <param name="OneFix">What a template fault usually is, for the "One fix" line; null when the check doesn't say.</param>
/// <param name="IsPlain">The words are the check's plain ones, not its technical texts standing in.</param>
internal sealed record CheckWords(string Title, IReadOnlyList<string> What, IReadOnlyList<string> Fix, Fixer? Fixer, string Passed, string? OneFix, bool IsPlain);
