namespace ShoutingIguana.Engine.Analysis.Models;

/// <summary>
/// A check in plain words, for a reader who isn't an SEO (docs/v2-ui-simple.md, "Plain words"): beside the catalog's
/// precise texts, never in place of them. <see cref="IssuePlainTexts"/> holds them, by check id.
/// </summary>
/// <param name="Title">The problem as its owner would say it: sentence case, no count, 60 characters at most.</param>
/// <param name="What">
/// What's wrong and why it matters, 2 to 4 sentences, a sentence a string. The first says what is wrong and stands alone,
/// in 120 characters at most: lists show only it.
/// </param>
/// <param name="Fix">How to fix it, 1 to 4 steps, a step a string: each one sentence that starts with a verb.</param>
/// <param name="Fixer">Who usually makes the fix.</param>
/// <param name="Passed">The good news when the check finds nothing, as a fact that stays true when it had nothing to look at.</param>
public sealed record IssuePlainText(string Title, IReadOnlyList<string> What, IReadOnlyList<string> Fix, IssueFixer Fixer, string Passed)
{
    /// <summary>What a fault in the site's template usually is, in one sentence, for a check where one fix can clear most pages. Optional.</summary>
    public string? OneFix { get; init; }

    /// <summary>
    /// What the check looks for, as a short noun phrase ("A missing sitemap"), 60 characters at most: for a list of checks
    /// that couldn't run, where a title that is a sentence would read as a finding. Optional.
    /// </summary>
    public string? LooksFor { get; init; }
}
