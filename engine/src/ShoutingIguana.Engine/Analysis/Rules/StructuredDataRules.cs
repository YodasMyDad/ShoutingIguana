using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;

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
        foreach (var block in page.Facts.JsonLd.Where(block => !string.IsNullOrWhiteSpace(block)))
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
                if (page.IsIndexable)
                {
                    RichResultRequirements.Check(document.RootElement, missing);
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
}
