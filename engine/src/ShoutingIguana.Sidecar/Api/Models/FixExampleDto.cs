using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Sidecar.Api.Models;

/// <summary>A check's fix as code (<see cref="FixExample"/>): the code as it is now and as it should be.</summary>
/// <param name="Language">What the code is written in: <c>html</c>, <c>httpHeader</c>, <c>robotsTxt</c>, <c>xml</c> or <c>json</c>.</param>
/// <param name="After">The corrected code.</param>
/// <param name="Before">The code as it is now, or null when the fix adds something that isn't there at all.</param>
/// <param name="When">Which case this is, for a check with several; null otherwise.</param>
/// <param name="Where">Where the code goes when that isn't obvious, written to follow a label ("in the page's &lt;head&gt;"); null when it is.</param>
/// <param name="Case">A short stable key for the case ("icon-link"); null for a check with one example.</param>
internal sealed record FixExampleDto(FixLanguage Language, string After, string? Before, string? When, string? Where, string? Case)
{
    public static FixExampleDto From(FixExample example) =>
        new(example.Language, example.After, example.Before, example.When, example.Where, example.Case);
}
