using System.Globalization;
using System.Text;
using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// JSON-LD that doesn't parse, rich result types missing what Google requires (indexable pages only), and since batch 5:
/// what isn't schema.org's (any page), what Google recommends that's missing (indexable pages), and rich result types on a
/// noindex page. Microdata items are read as JSON-LD blocks are (change C5); they have no syntax of their own to break.
/// </summary>
internal sealed class StructuredDataRules : IPageRule
{
    private const int MAX_FAULTS = 3;

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        string? syntaxError = null;
        List<string> missing = [];
        List<string> faults = [];
        List<string> lacking = [];
        List<string> richTypes = [];
        var noindex = page.Facts.Robots.NoIndex;

        // A block at the length cap was cut short when the page was parsed, so it can't be judged. Each JSON-LD block keeps
        // its place on the page, for a detail to say which failed; a Microdata item, read as a block, has none (0).
        var jsonLd = page.Facts.JsonLd;
        var blocks = jsonLd.Select((block, index) => (Block: block, Number: index + 1))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Block) && entry.Block.Length < PageWalker.MAX_JSON_LD_CHARS)
            .Concat(page.Facts.Microdata.Select(block => (Block: block, Number: 0)));
        foreach (var (block, number) in blocks)
        {
            JsonDocument document;
            try
            {
                // Valid JSON only. A trailing comma is the error Google's unparsable structured data report lists.
                document = JsonDocument.Parse(block);
            }
            catch (JsonException exception)
            {
                // Page content is untrusted input, so a parse failure is an expected outcome here, not a bug.
                syntaxError ??= InBlock(number, jsonLd.Count, Where(exception, block), IssueFaults.WithoutPosition(exception.Message));
                continue;
            }

            using (document)
            {
                try
                {
                    // Every page's names are read, as an unreadable one is a syntax fault, whatever the page's indexability.
                    ReadNames(document.RootElement);
                    SchemaChecks.Faults(document.RootElement, faults);
                    if (page.IsIndexable)
                    {
                        RichResultRequirements.Check(document.RootElement, missing);
                        SchemaChecks.Recommended(document.RootElement, lacking);
                    }

                    if (noindex)
                    {
                        richTypes.AddRange(RichResultRequirements.TopLevelEntities(document.RootElement).SelectMany(RichResultRequirements.Types)
                            .Where(RichResultRequirements.HasRichResult));
                    }
                }
                catch (InvalidOperationException)
                {
                    // A property name escaping half a character (\uDC00) parses, but can't be read back as a name.
                    syntaxError ??= InBlock(number, jsonLd.Count, null, "A property name escapes an invalid character, such as half of a surrogate pair");
                }
            }
        }

        if (syntaxError is not null)
        {
            yield return new IssueHit(IssueCatalog.STRUCTURED_DATA_INVALID, page.UrlId, syntaxError);
        }

        if (missing.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.STRUCTURED_DATA_MISSING_REQUIRED, page.UrlId, string.Join("; ", missing.Distinct()));
        }

        if (faults.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.STRUCTURED_DATA_SCHEMA_ERRORS, page.UrlId, FaultsText(faults));
        }

        if (lacking.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.STRUCTURED_DATA_MISSING_RECOMMENDED, page.UrlId, RuleText.Fit(string.Join("; ", lacking.Distinct(StringComparer.Ordinal))));
        }

        if (richTypes.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.STRUCTURED_DATA_ON_NOINDEX, page.UrlId, RuleText.Fit(string.Join(", ", richTypes.Distinct(StringComparer.Ordinal))));
        }
    }

    /// <summary>
    /// A block's syntax error, saying which block it's in and where, as the page has them, before what the parser said:
    /// "Block 2 of 3, line 1, position 55: '"' is invalid after a value. Expected either ',', '}', or ']'." A Microdata
    /// item's (number 0) is the message alone. The fault its group is, IssueFaults.JsonError reads from it.
    /// </summary>
    private static string InBlock(int number, int blocks, string? where, string message) => number == 0
        ? message
        : string.Create(CultureInfo.InvariantCulture, $"Block {number:N0} of {blocks:N0}{(where is null ? string.Empty : ", " + where)}: {message}");

    /// <summary>
    /// Where in its block the parser failed, counted from 1 as an editor counts: "line 3, position 18". The parser counts
    /// lines from 0, and the bytes of UTF-8 on the line, which are the characters only in ASCII. Null when it doesn't say.
    /// </summary>
    private static string? Where(JsonException exception, string block)
    {
        if (exception.LineNumber is not { } line || exception.BytePositionInLine is not { } bytes)
        {
            return null;
        }

        // The parser counts a line at each line feed, as this does.
        var start = 0;
        for (var skipped = 0L; skipped < line && start >= 0; skipped++)
        {
            var feed = block.IndexOf('\n', start);
            start = feed < 0 ? -1 : feed + 1;
        }

        var end = start < 0 ? -1 : block.IndexOf('\n', start);
        var text = start < 0 ? string.Empty : end < 0 ? block[start..] : block[start..end];
        var utf8 = Encoding.UTF8.GetBytes(text);
        var characters = Encoding.UTF8.GetCharCount(utf8, 0, (int)Math.Min(bytes, utf8.Length));
        return string.Create(CultureInfo.InvariantCulture, $"line {line + 1:N0}, position {characters + 1:N0}");
    }

    /// <summary>
    /// The first faults, up to three, as many as fit a detail whole, and how many more: "Organisation isn't a schema.org type;
    /// Offer: priceCurrency "Dollars" isn't ISO 4217; and 2 more".
    /// </summary>
    private static string FaultsText(List<string> faults)
    {
        var distinct = faults.Distinct(StringComparer.Ordinal).ToList();
        List<string> shown = [];
        foreach (var fault in distinct.Take(MAX_FAULTS))
        {
            var more = distinct.Count - shown.Count - 1;
            var candidate = string.Join("; ", [.. shown, fault]) + (more > 0 ? $"; and {more:N0} more" : string.Empty);
            if (shown.Count > 0 && candidate.Length > RuleText.DETAIL_MAX)
            {
                break;
            }

            shown.Add(fault);
        }

        var rest = distinct.Count - shown.Count;
        return RuleText.Fit(string.Join("; ", shown) + (rest > 0 ? $"; and {rest:N0} more" : string.Empty));
    }

    /// <summary>Reads every property name, which throws for one escaping half a surrogate pair. JsonDocument caps the depth.</summary>
    private static void ReadNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = property.Name;
                    ReadNames(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ReadNames(item);
                }

                break;
        }
    }
}
