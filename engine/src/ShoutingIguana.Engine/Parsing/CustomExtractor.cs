using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.XPath;
using ShoutingIguana.Engine.Configuration;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Applies a crawl's extraction rules to the document a page was just parsed into. CSS and XPath read that document.
/// A regular expression reads the HTML string the parser was given. Nothing parses the HTML again.
/// </summary>
internal static class CustomExtractor
{
    /// <summary>Values kept from one rule on one page. The count still says how many matched.</summary>
    public const int MAX_VALUES = 50;

    /// <summary>Characters kept of one value.</summary>
    public const int MAX_VALUE_LENGTH = 2_000;

    /// <summary>The rules compiled once for a crawl, safe for the workers to apply at the same time.</summary>
    public static ExtractionSession Open(IReadOnlyList<ExtractionRule>? rules) => new(rules);
}

/// <summary>One crawl's rules, compiled, with a rule that fails marked so later pages skip it.</summary>
internal sealed class ExtractionSession
{
    private readonly Rule[] _rules;

    public ExtractionSession(IReadOnlyList<ExtractionRule>? rules)
    {
        _rules = (rules ?? []).Where(rule => rule.Enabled).Select(rule => rule.Trimmed()).Where(rule => rule.Name.Length > 0 && rule.Selector.Length > 0)
            .Select(rule => new Rule(rule, rule.SelectorType == ExtractionSelector.Regex ? ExtractionSyntax.CompileRegex(rule.Selector) : null))
            .ToArray();
    }

    public bool Any => _rules.Length > 0;

    /// <summary>What each enabled rule found. A rule with no match is left out. A rule that fails is included once, then skipped.</summary>
    public IReadOnlyList<ExtractionMatch> Apply(IDocument document, string html)
    {
        if (_rules.Length == 0)
        {
            return [];
        }

        List<ExtractionMatch> matches = [];
        foreach (var rule in _rules)
        {
            if (Volatile.Read(ref rule.Failed) == 1)
            {
                continue;
            }

            try
            {
                var values = rule.Kind switch
                {
                    ExtractionSelector.Css => ByCss(document, rule.Selector),
                    ExtractionSelector.XPath => ByXPath(document, rule.Selector),
                    ExtractionSelector.Regex => ByRegex(html, rule.Regex!),
                    _ => []
                };
                if (values.Count > 0)
                {
                    matches.Add(new ExtractionMatch(rule.Name, rule.Kind, rule.Selector, values.Take(CustomExtractor.MAX_VALUES).ToList(), values.Count, null));
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (Interlocked.Exchange(ref rule.Failed, 1) == 0)
                {
                    matches.Add(new ExtractionMatch(rule.Name, rule.Kind, rule.Selector, [], 0, Failure(rule.Name, exception)));
                }
            }
        }

        return matches;
    }

    private static List<string> ByCss(IDocument document, string selector)
    {
        List<string> values = [];
        foreach (var node in document.QuerySelectorAll(selector))
        {
            Add(values, node.TextContent);
        }

        return values;
    }

    private static List<string> ByXPath(IDocument document, string xpath)
    {
        List<string> values = [];
        if (document.DocumentElement is not { } root)
        {
            return values;
        }

        foreach (var node in root.SelectNodes(xpath))
        {
            Add(values, node.TextContent);
        }

        return values;
    }

    private static List<string> ByRegex(string html, Regex regex)
    {
        List<string> values = [];
        foreach (Match match in regex.Matches(html))
        {
            var group = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            Add(values, WebUtility.HtmlDecode(group));
        }

        return values;
    }

    private static void Add(List<string> values, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();
        values.Add(trimmed.Length <= CustomExtractor.MAX_VALUE_LENGTH ? trimmed : trimmed[..CustomExtractor.MAX_VALUE_LENGTH]);
    }

    private static string Failure(string name, Exception exception)
    {
        var detail = exception is RegexMatchTimeoutException
            ? "The regular expression took too long and was stopped."
            : exception.Message.Split('\n', 2)[0].Trim();
        if (detail.Length > 200)
        {
            detail = detail[..200] + "…";
        }

        return $"The extraction rule '{name}' failed and was not tried on later pages. {detail}";
    }

    private sealed class Rule(ExtractionRule rule, Regex? regex)
    {
        public string Name { get; } = rule.Name;
        public ExtractionSelector Kind { get; } = rule.SelectorType;
        public string Selector { get; } = rule.Selector;
        public Regex? Regex { get; } = regex;
        public int Failed;
    }
}
