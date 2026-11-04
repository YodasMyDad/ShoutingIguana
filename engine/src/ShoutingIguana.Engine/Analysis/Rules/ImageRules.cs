using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>Alt text, dimensions, broken and oversized images, on every analysed page.</summary>
internal sealed class ImageRules(AnalysisSettings settings, UrlTable urls) : IPageRule
{
    public IEnumerable<IssueHit> Check(AnalyzedPage page)
    {
        int missingAlt = 0, longAlt = 0, missingSize = 0;
        foreach (var image in page.Facts.Images)
        {
            if (image.Url is null || IsTrackingPixel(image))
            {
                continue;
            }

            if (!image.HasAlt)
            {
                missingAlt++;
            }
            else if (image.Alt is { } alt && RuleText.Length(alt) > settings.ImageAltMaxLength)
            {
                longAlt++;
            }

            if (string.IsNullOrWhiteSpace(image.Width) || string.IsNullOrWhiteSpace(image.Height))
            {
                missingSize++;
            }
        }

        if (missingAlt > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_MISSING_ALT, page.UrlId, RuleText.Count(missingAlt, "image", "images"));
        }

        if (longAlt > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_ALT_TOO_LONG, page.UrlId, RuleText.Count(longAlt, "image", "images"));
        }

        if (missingSize > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_MISSING_DIMENSIONS, page.UrlId, RuleText.Count(missingSize, "image", "images"));
        }

        TargetSet broken = new(), large = new();
        foreach (var link in page.Links)
        {
            if (link.Kind != LinkKind.Image)
            {
                continue;
            }

            // An image that redirects is judged by where the redirects end.
            if (urls.IsBrokenThroughRedirects(link.ToId))
            {
                broken.Add(link.ToId);
            }
            else if (urls[urls.TraceRedirects(link.ToId).FinalId].SizeBytes > settings.LargeImageBytes)
            {
                large.Add(link.ToId);
            }
        }

        if (broken.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_BROKEN, page.UrlId, RuleText.BrokenTargets(urls, broken, "image", "images"), broken.First) { Targets = broken.Ids };
        }

        if (large.Count > 0)
        {
            yield return new IssueHit(IssueCatalog.IMAGE_TOO_LARGE, page.UrlId,
                $"{RuleText.Count(large.Count, "image", "images")} over {RuleText.Size(settings.LargeImageBytes)}", large.First) { Targets = large.Ids };
        }
    }

    /// <summary>1x1 analytics beacons aren't content, so they need neither alt text nor layout space.</summary>
    private static bool IsTrackingPixel(ImageFact image) => image.Width is "0" or "1" && image.Height is "0" or "1";
}
