using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Reporting.Models;

/// <summary>
/// A check's words as the report prints them: its plain words (<see cref="IssuePlainText"/>), every limit in them said
/// as the analysis ran with it. Where a check has none, its technical texts stand in (<see cref="IsPlain"/> says which),
/// and the technical title is printed beside either.
/// </summary>
/// <param name="Title">The problem as its owner would say it.</param>
/// <param name="What">What's wrong and why it matters, a sentence or a paragraph each.</param>
/// <param name="Fix">How to fix it, a step each.</param>
/// <param name="Fixer">Who usually does it; null when the check doesn't say.</param>
/// <param name="Passed">The good news when the check finds nothing, as a fact: "Every page has a title".</param>
/// <param name="OneFix">What a template fault usually is, for the "One fix" line; null when the check doesn't say.</param>
/// <param name="IsPlain">The words are the check's plain ones, not its technical texts standing in.</param>
internal sealed record CheckWords(string Title, IReadOnlyList<string> What, IReadOnlyList<string> Fix, IssueFixer? Fixer, string Passed, string? OneFix, bool IsPlain)
{
    /// <summary>What the check looks for, as a noun phrase ("A missing sitemap"), for a list of checks that couldn't run; null when it has none.</summary>
    public string? LooksFor { get; init; }

    /// <summary>The check as a list of checks that couldn't run names it: by what it looks for, or by its title.</summary>
    public string Named => LooksFor ?? Title;
}
