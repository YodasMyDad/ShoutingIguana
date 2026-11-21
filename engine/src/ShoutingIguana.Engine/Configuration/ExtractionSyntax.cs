using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using AngleSharp.XPath;
using ShoutingIguana.Engine.Configuration.Models;

namespace ShoutingIguana.Engine.Configuration;

/// <summary>
/// Whether an extraction rule's selector can be used at all, checked once when the crawl's options are checked.
/// A rule that passes here can still fail on one page; that is stored against the page and the rule is not tried again.
/// </summary>
internal static class ExtractionSyntax
{
    /// <summary>The problem with this rule, or null when it can be applied.</summary>
    public static string? Problem(ExtractionRule rule)
    {
        var name = rule.Name.Trim();
        var selector = rule.Selector.Trim();
        if (name.Length == 0)
        {
            return "Give the extraction rule a name.";
        }

        if (name.Length > ExtractionRule.MAX_NAME_LENGTH)
        {
            return $"The extraction rule '{Cut(name)}' has a name longer than {ExtractionRule.MAX_NAME_LENGTH} characters.";
        }

        if (!Enum.IsDefined(rule.SelectorType))
        {
            return $"The extraction rule '{name}' needs a selector type of css, xPath or regex.";
        }

        if (selector.Length == 0)
        {
            return $"The extraction rule '{name}' needs a selector.";
        }

        if (selector.Length > ExtractionRule.MAX_SELECTOR_LENGTH)
        {
            return $"The extraction rule '{name}' has a selector longer than {ExtractionRule.MAX_SELECTOR_LENGTH} characters.";
        }

        return rule.SelectorType switch
        {
            ExtractionSelector.Css => CssProblem(name, selector),
            ExtractionSelector.XPath => XPathProblem(name, selector),
            ExtractionSelector.Regex => RegexProblem(name, selector),
            _ => $"The extraction rule '{name}' needs a selector type of css, xPath or regex."
        };
    }

    /// <summary>A regular expression as the crawl runs it: case-insensitive, and non-backtracking when the pattern allows it.</summary>
    public static Regex CompileRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
    }

    private static string? CssProblem(string name, string selector)
    {
        try
        {
            using var document = new HtmlParser().ParseDocument("<html></html>");
            _ = document.QuerySelectorAll(selector);
            return null;
        }
        catch (Exception exception)
        {
            return $"The extraction rule '{name}' has a CSS selector that can't be used. {FirstLine(exception.Message)}";
        }
    }

    private static string? XPathProblem(string name, string selector)
    {
        try
        {
            using var document = new HtmlParser().ParseDocument("<html></html>");
            _ = document.DocumentElement.SelectNodes(selector);
            return null;
        }
        catch (Exception exception)
        {
            return $"The extraction rule '{name}' has an XPath expression that can't be used. {FirstLine(exception.Message)}";
        }
    }

    private static string? RegexProblem(string name, string selector)
    {
        try
        {
            _ = CompileRegex(selector);
            return null;
        }
        catch (ArgumentException exception)
        {
            return $"The extraction rule '{name}' has a regular expression that can't be used. {FirstLine(exception.Message)}";
        }
    }

    private static string Cut(string name) => name.Length <= 40 ? name : name[..40] + "…";

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length <= 200 ? line : line[..200] + "…";
    }
}
