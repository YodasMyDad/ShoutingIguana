using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>JSON-LD that doesn't parse, and rich result types missing what Google requires (indexable pages only).</summary>
internal sealed class StructuredDataRules : IPageRule
{
    // Lenient where browsers and Google are: comments and trailing commas don't make a block unusable.
    private static readonly JsonDocumentOptions OPTIONS = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        string? syntaxError = null;
        List<string> missing = [];
        // A block at the length cap was cut short when the page was parsed, so it can't be judged.
        foreach (var block in page.Facts.JsonLd.Where(block => !string.IsNullOrWhiteSpace(block) && block.Length < PageWalker.MAX_JSON_LD_CHARS))
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(block, OPTIONS);
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
                    if (page.IsIndexable)
                    {
                        RichResultRequirements.Check(document.RootElement, missing);
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
