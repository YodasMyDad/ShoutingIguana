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

        // A block at the length cap was cut short when the page was parsed, so it can't be judged.
        var blocks = page.Facts.JsonLd.Where(block => !string.IsNullOrWhiteSpace(block) && block.Length < PageWalker.MAX_JSON_LD_CHARS);
        foreach (var block in blocks.Concat(page.Facts.Microdata))
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
                syntaxError ??= exception.Message;
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
                    syntaxError ??= "A property name escapes an invalid character, such as half of a surrogate pair";
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
