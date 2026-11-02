namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A read gave up on a regular expression: one of its matches took longer than <see cref="ReaderFunctions.MATCH_TIMEOUT"/>,
/// or its matches took longer than the read's budget in all. The readers turn it into a <see cref="UrlQueryException"/> on
/// the filters with that pattern.
/// </summary>
internal sealed class RegexTooComplexException(string pattern, Exception? inner = null)
    : Exception($"The pattern '{pattern}' is too complex to run on this crawl.", inner)
{
    public string Pattern { get; } = pattern;
}
