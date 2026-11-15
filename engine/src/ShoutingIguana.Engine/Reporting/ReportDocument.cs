using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShoutingIguana.Engine.Analysis;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Reporting.Models;
using ShoutingIguana.Engine.Storage.Models;
using static ShoutingIguana.Engine.Reporting.ReportWords;

namespace ShoutingIguana.Engine.Reporting;

/// <summary>
/// The report: the one document a crawl ends in, as a single self-contained HTML file. Inline CSS, inline SVG, no
/// script, no web font, light only; it prints to A4 and Letter from the system's print dialog, which is also how it
/// becomes a PDF. Its design is <c>app/design/simple/report.html</c>, and its outline is the design document's
/// (<c>docs/v2-ui-simple.md</c>, "The report"): the head, the summary, what to fix first, how the score is worked out,
/// then from a new page every check that found something, what couldn't be checked, what found nothing wrong, and how
/// the crawl was made. Its length follows the checks that found something, never the site's size.
/// <para>
/// <b>What a site sends can't become markup.</b> Every string is escaped where it's written (<see cref="HtmlText"/>); a
/// URL is a link only when it's http or https; and the document carries a Content-Security-Policy that allows no
/// script, no network and no style but its own one sheet, named by its hash: there isn't a style attribute in it.
/// </para>
/// </summary>
internal static class ReportDocument
{
    /// <summary>The most characters of a URL or of what was found that an example shows; the workbook has all of it.</summary>
    private const int SHOWN_URL = 160;

    private const int SHOWN_DETAIL = 300;

    /// <summary>The problems "Fix these first" lists.</summary>
    private const int FIRST = 5;

    private const double RING_RADIUS = 44;

    private static readonly string[] COUNT_WORDS = ["none", "one", "two", "three", "four", "five"];

    private const string CSS_SOURCE = """
          :root { --text: #18181B; --secondary: #46464D; --muted: #68686F; --rule: #E3E3E7; --rule-soft: #EEEEF1; --sunken: #FAFAFB; --accent: #0B6BD3; --accent-subtle: #EBF4FF; --accent-border: #A8CFFF; }
          * { box-sizing: border-box; }
          html { -webkit-text-size-adjust: 100%; }
          body { margin: 0; background: #fff; color: var(--text); font: 10pt/1.45 Inter, "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif; -webkit-font-smoothing: antialiased; }
          .sheet { max-width: 760px; margin: 0 auto; padding: 44px 40px 64px; }
          h1, h2, h3, h4, p, ul, ol, dl, dd, figure, table { margin: 0; }
          ul, ol { padding: 0; list-style: none; }
          a { color: inherit; text-decoration: none; }
          a:hover { text-decoration: underline; }
          .mono { font-family: "JetBrains Mono", "Cascadia Mono", Consolas, monospace; font-size: 0.88em; overflow-wrap: anywhere; }
          .muted { color: var(--muted); }
          .num { font-variant-numeric: tabular-nums; }
          .mark { flex: none; vertical-align: -2px; }
          .ring { flex: none; }

          .eyebrow { font-size: 9pt; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }
          h1 { margin-top: 4px; font-size: 24pt; line-height: 1.15; font-weight: 600; letter-spacing: -0.02em; overflow-wrap: anywhere; }
          .meta { margin-top: 8px; color: var(--secondary); }
          .note { margin-top: 10px; padding: 8px 12px; border: 1px solid var(--rule); border-radius: 8px; background: var(--sunken); font-size: 9.5pt; color: var(--secondary); }
          h2 { margin: 20px 0 4px; font-size: 14pt; line-height: 1.25; font-weight: 600; letter-spacing: -0.01em; }
          h2 + .lead { margin-bottom: 12px; color: var(--secondary); }
          .lead { max-width: 62ch; }
          .lead--group { margin-top: 6px; }

          .summary { display: grid; grid-template-columns: 1.5fr 1fr; margin-top: 18px; border: 1px solid var(--rule); border-radius: 10px; }
          .summary__main { display: flex; gap: 20px; align-items: center; padding: 18px 22px; }
          .summary__word { font-size: 17pt; line-height: 1.2; font-weight: 600; letter-spacing: -0.015em; }
          .summary__sentence { margin-top: 4px; font-size: 11.5pt; line-height: 1.4; }
          .summary__covers { margin-top: 8px; font-size: 9.5pt; color: var(--secondary); }
          .tally { padding: 16px 20px; border-left: 1px solid var(--rule-soft); display: grid; align-content: center; gap: 7px; font-size: 10pt; color: var(--secondary); }
          .tally li { display: grid; grid-template-columns: 16px 30px 1fr; gap: 8px; align-items: center; }
          .tally b { text-align: right; font-size: 12pt; font-weight: 500; color: var(--text); font-variant-numeric: tabular-nums; }

          .first { margin-top: 8px; border: 1px solid var(--rule); border-radius: 10px; }
          .first li { display: grid; grid-template-columns: 18px 16px 1fr auto; gap: 10px; align-items: baseline; padding: 6px 14px; border-top: 1px solid var(--rule-soft); }
          .first li:first-child { border-top: 0; }
          .first__n { color: var(--muted); text-align: right; font-variant-numeric: tabular-nums; }
          .first__title { font-weight: 600; }
          .first__count { color: var(--secondary); white-space: nowrap; font-variant-numeric: tabular-nums; }
          .first__foot { margin-top: 6px; font-size: 9pt; color: var(--secondary); }

          table { width: 100%; border-collapse: collapse; font-size: 9.5pt; line-height: 1.4; }
          th { padding: 6px 10px; text-align: left; font-size: 8.5pt; font-weight: 600; color: var(--muted); border-bottom: 1px solid var(--rule); }
          td { padding: 5px 10px; vertical-align: top; border-bottom: 1px solid var(--rule-soft); }
          td.num, th.num { text-align: right; white-space: nowrap; }
          tfoot td { border-bottom: 0; font-weight: 600; font-size: 10.5pt; }
          .score-table { margin-top: 10px; }
          .score-note { margin-top: 6px; font-size: 9pt; color: var(--secondary); }

          .group-title { display: flex; align-items: center; gap: 8px; margin: 20px 0 0; padding-bottom: 6px; border-bottom: 2px solid var(--text); font-size: 12pt; font-weight: 600; }
          .group-title span { font-weight: 400; color: var(--muted); }
          .problem { padding: 13px 0 14px; border-bottom: 1px solid var(--rule); }
          .problem h3 { display: flex; align-items: baseline; gap: 8px; font-size: 11.5pt; line-height: 1.3; font-weight: 600; letter-spacing: -0.005em; }
          .problem__tech { margin-left: auto; padding-left: 12px; font-size: 8.5pt; font-weight: 400; letter-spacing: 0; color: var(--muted); white-space: nowrap; }
          .problem__meta { margin-top: 2px; font-size: 9pt; color: var(--muted); }
          .problem__meta b { color: var(--text); font-weight: 600; }
          .problem h4 { margin: 8px 0 1px; font-size: 9pt; font-weight: 600; }
          .problem p, .problem .steps { font-size: 9.5pt; line-height: 1.42; }
          .problem p + p { margin-top: 4px; }
          .problem .problem__meta, .problem .more { font-size: 9pt; }
          .problem .more { font-size: 8.5pt; }
          .steps { counter-reset: s; display: grid; gap: 1px; }
          .steps li { counter-increment: s; display: grid; grid-template-columns: 16px 1fr; gap: 6px; }
          .steps li::before { content: counter(s) "."; color: var(--muted); font-variant-numeric: tabular-nums; }
          .problem .onefix { margin-top: 10px; padding: 9px 12px; border: 1px solid var(--accent-border); border-radius: 8px; background: var(--accent-subtle); font-size: 9.5pt; }
          .onefix b { font-weight: 600; }
          .examples { margin-top: 8px; font-size: 9pt; }
          .examples td, .examples th { padding: 3.5px 8px; }
          .examples caption { caption-side: top; padding: 0 0 2px; text-align: left; font-size: 9pt; font-weight: 600; }
          .examples td:first-child { width: 56%; }
          .examples--where td:first-child { width: 34%; }
          .examples--where td:last-child { width: 40%; }
          .problem__split { display: grid; grid-template-columns: 1.2fr 1fr; gap: 22px; }
          .problem__split .examples { margin-top: 8px; }
          .examples--one td:first-child { width: auto; }
          .examples--one .found { display: block; color: var(--muted); }
          .examples td, .examples--one .found, .problem .onefix { overflow-wrap: anywhere; }
          .more { margin-top: 4px; font-size: 8.5pt; color: var(--muted); }

          .plain-list li { display: grid; grid-template-columns: 16px 1fr; gap: 8px; padding: 7px 0; border-bottom: 1px solid var(--rule-soft); }
          .plain-list b { font-weight: 600; }
          .passed { display: grid; grid-template-columns: 1fr 1fr; gap: 0 28px; padding-top: 9px; font-size: 9.5pt; }
          .passed li { display: grid; grid-template-columns: 14px 1fr; gap: 7px; padding: 2px 0; }
          .passed h4 { margin: 0 0 2px; font-size: 8.5pt; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: 0.05em; }
          .facts { display: grid; grid-template-columns: 200px 1fr; font-size: 9.5pt; }
          .facts dt, .facts dd { padding: 5px 0; border-bottom: 1px solid var(--rule-soft); }
          .facts dt { color: var(--muted); }
          .foot { margin-top: 32px; padding-top: 10px; border-top: 1px solid var(--rule); font-size: 8.5pt; color: var(--muted); display: flex; justify-content: space-between; gap: 16px; }
          .sheet--summary .foot { margin-top: 12px; padding-top: 8px; }
          .sheet--summary h2 { margin-top: 14px; }
          .sheet--summary .summary { margin-top: 12px; }
          .sheet--summary .first li { padding: 5px 14px; }
          .sheet--summary .score-table td { padding: 4px 10px; }

          @page { margin: 16mm 15mm 16mm; }
          @media print {
            .sheet { max-width: none; padding: 0; }
            h2, h3, h4, .group-title { break-after: avoid; }
            .summary, .first, .problem--compact, .keep, .passed, .plain-list li, tr, .onefix, .facts, .note { break-inside: avoid; }
            .new-page { break-before: page; }
            .new-page > h2:first-child { margin-top: 0; padding-top: 2px; }
            .score-table tfoot { display: table-row-group; }
            a:hover { text-decoration: none; }
          }

        """;

    /// <summary>
    /// The sheet as the document holds it, its lines ended as a browser reads them: a browser takes the hash of a style
    /// element's text after it has turned every line ending into a line feed, so the sheet is written, and hashed, that way.
    /// </summary>
    private static readonly string CSS = CSS_SOURCE.ReplaceLineEndings("\n");

    /// <summary>
    /// The policy the document carries: nothing may be loaded or run, and the only style is the document's own sheet, by
    /// its hash. No frame, form or base URL either.
    /// </summary>
    public static string ContentSecurityPolicy { get; } =
        $"default-src 'none'; style-src 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(CSS)))}'; base-uri 'none'; form-action 'none'";

    /// <summary>The report of a crawl, as one HTML document.</summary>
    public static string Write(ReportModel model, ReportOptions options)
    {
        var html = new StringBuilder(96 * 1_024);
        var full = options.Cut == ReportCut.Full;
        var checkedOn = Date(model.StartedAt, model.TimeZone);

        html.Append("<!doctype html>\n<html lang=\"en-GB\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").AppendText(ContentSecurityPolicy).Append("\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">\n")
            .Append("<meta name=\"generator\" content=\"Shouting Iguana ").AppendText(model.EngineVersion).Append("\">\n")
            .Append("<title>Website check: ").AppendText(model.Site).Append(", ").Append(checkedOn).Append("</title>\n")
            .Append("<style>").Append(CSS).Append("</style>\n</head>\n<body>\n").Append(full ? "<main class=\"sheet\">\n" : "<main class=\"sheet sheet--summary\">\n");

        Head(html, model, checkedOn);
        Summary(html, model, full);
        FixFirst(html, model, full);
        if (model.Score is not null)
        {
            HowTheScoreIsWorkedOut(html, model);
        }

        if (full)
        {
            WhatToFix(html, model, options);
            CouldNotBeChecked(html, model);
            FoundNothingWrong(html, model);
            AboutThisCheck(html, model);
        }

        html.Append("  <footer class=\"foot\"><span>Made with Shouting Iguana ").AppendText(model.EngineVersion).Append(" on ").Append(Date(model.MadeAt, model.TimeZone))
            .Append("</span><span>Score version ").Append(model.Score?.Version ?? ScoreCalculator.VERSION).Append(" · engine ").AppendText(model.AnalysisEngineVersion)
            .Append("</span></footer>\n</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    // ---- 1. The head.

    private static void Head(StringBuilder html, ReportModel model, string checkedOn)
    {
        html.Append("  <header>\n    <p class=\"eyebrow\">Website check</p>\n    <h1>").AppendText(model.Site).Append("</h1>\n    <p class=\"meta\">Checked on ").Append(checkedOn)
            .Append(" · ").Append(Plural(model.Pages, "page", "pages")).Append(" read · ").Append(Plural(model.Urls, "address", "addresses")).Append(" checked · ")
            .Append(model.ChecksRun).Append(" of ").Append(model.ChecksInCatalog).Append(" checks run</p>\n");
        if (!model.Finished && !model.CrawlWentOn)
        {
            html.Append("    <p class=\"note\">").Append(Unfinished(model)).Append(" This report covers only what was read.</p>\n");
        }

        if (model.OutOfDate.Count > 0)
        {
            html.Append("    <p class=\"note\">These results were worked out on ").Append(Date(model.AnalyzedAt, model.TimeZone)).Append(", and ")
                .Append(model.OutOfDate.Contains(OutOfDateReason.CrawlChanged) ? "the site check has gone on since" : "the checks have changed since")
                .Append(": work out the results again for a report that is up to date.</p>\n");
        }

        html.Append("  </header>\n\n");
    }

    /// <summary>How a crawl that didn't read everything stands: stopped early, at its limit, still running, or failed.</summary>
    private static string Unfinished(ReportModel model)
    {
        var read = $"{Plural(model.Pages, "page", "pages")} read";
        var left = model.Queued > 0 ? $", {Plural(model.Queued, "address", "addresses")} still to check" : string.Empty;
        return model.Status switch
        {
            CrawlStatus.Completed when model.NotRead.GetValueOrDefault(SkipReason.UrlLimit) > 0 =>
                $"This check stopped at its limit of {Number(model.Options.MaxUrls)} addresses: {read}, {Plural(model.NotRead.GetValueOrDefault(SkipReason.UrlLimit), "address", "addresses")} past the limit.",
            CrawlStatus.Completed =>
                $"This check stopped at its limit of {Plural(model.Options.MaxDepth, "click", "clicks")} from the start: {read}, {Plural(model.NotRead.GetValueOrDefault(SkipReason.DepthLimit), "address", "addresses")} further in than that.",
            CrawlStatus.Failed => $"This check failed before it finished: {read}{left}.",
            CrawlStatus.Stopped => $"This check was stopped early: {read}{left}.",
            _ => $"This check hasn’t finished: {read}{left}."
        };
    }

    // ---- 2. The summary.

    private static void Summary(StringBuilder html, ReportModel model, bool full)
    {
        var (errors, warnings, notices) = (model.ChecksOf(IssueSeverity.Error), model.ChecksOf(IssueSeverity.Warning), model.ChecksOf(IssueSeverity.Notice));
        html.Append("  <section class=\"summary\" aria-label=\"Summary\">\n    <div class=\"summary__main\">\n      ");
        Ring(html, model.Score);
        html.Append("\n      <div>\n");
        if (model.Score is { } score)
        {
            html.Append("        <p class=\"summary__word\">").Append(Band(score.Band)).Append(score.Partial ? ", so far" : string.Empty).Append("</p>\n")
                .Append("        <p class=\"summary__sentence\">").Append(Lead(score.Band)).Append(' ').Append(Found(errors, warnings, notices)).Append("</p>\n")
                .Append("        <p class=\"summary__covers\">").Append(Covers(score, full)).Append("</p>\n");
        }
        else if (model.NoScore == NoScoreReason.NotScored)
        {
            html.Append("        <p class=\"summary__word\">No score</p>\n")
                .Append("        <p class=\"summary__sentence\">These results were worked out before the score was.</p>\n")
                .Append("        <p class=\"summary__covers\">An earlier version of Shouting Iguana worked out these results, and gave no score. Work out the results again to score the site.</p>\n");
        }
        else
        {
            html.Append("        <p class=\"summary__word\">No score</p>\n")
                .Append("        <p class=\"summary__sentence\">No page could be read, so there is nothing to score.</p>\n")
                .Append("        <p class=\"summary__covers\">The check asked for <span class=\"mono\">").AppendText(HtmlText.Cut(model.StartUrl, SHOWN_URL))
                .Append("</span> and got no page it could read. ").Append(model.BlockedByRobots > 0
                    ? $"The site’s rules file (robots.txt) keeps search engines out of {Plural(model.BlockedByRobots, "address", "addresses")}, which {(model.BlockedByRobots == 1 ? "wasn’t" : "weren’t")} read."
                    : "Check that the address is right and that the site is up, then check it again.")
                .Append("</p>\n");
        }

        html.Append("      </div>\n    </div>\n    <ul class=\"tally\">\n");
        Tally(html, "error", errors, errors == 1 ? "Serious problem" : "Serious problems");
        Tally(html, "warning", warnings, warnings == 1 ? "Problem worth fixing" : "Problems worth fixing");
        Tally(html, "notice", notices, notices == 1 ? "Thing worth a look" : "Things worth a look");
        Tally(html, "passed", model.Passed.Count, model.Passed.Count == 1 ? "Check found nothing wrong" : "Checks found nothing wrong");
        Tally(html, "skipped", model.Skipped.Count, model.Skipped.Count == 1 ? "Check couldn’t run" : "Checks couldn’t run");
        html.Append("    </ul>\n");
        if (model.HiddenProblems > 0)
        {
            // Hidden checks are left out of the lists and the counts, never out of the score, which is the analysis's.
            html.Append("    <p class=\"summary__covers\">")
                .Append(model.HiddenProblems == 1
                    ? "1 check that found something is hidden, so it’s left out of this report. The score still counts it."
                    : $"{model.HiddenProblems} checks that found something are hidden, so they’re left out of this report. The score still counts them.")
                .Append("</p>\n");
        }

        html.Append("  </section>\n\n");
    }

    private static void Tally(StringBuilder html, string mark, int count, string words)
    {
        html.Append("      <li>");
        Mark(html, mark, 14);
        html.Append("<b>").Append(count).Append("</b><span>").Append(words).Append("</span></li>\n");
    }

    /// <summary>"There are 2 serious problems and 4 problems worth fixing."</summary>
    private static string Found(int errors, int warnings, int notices) => (errors, warnings, notices) switch
    {
        (0, 0, 0) => "Nothing was found to fix.",
        (0, 0, _) => $"Nothing needs fixing, and there {(notices == 1 ? "is" : "are")} {Counted(IssueSeverity.Notice, notices)}.",
        (0, _, _) => $"There {(warnings == 1 ? "is" : "are")} {Counted(IssueSeverity.Warning, warnings)}.",
        (_, 0, _) => $"There {(errors == 1 ? "is" : "are")} {Counted(IssueSeverity.Error, errors)}.",
        _ => $"There {(errors == 1 ? "is" : "are")} {Counted(IssueSeverity.Error, errors)} and {Counted(IssueSeverity.Warning, warnings)}."
    };

    /// <summary>What the score covers: the pages and checks it's built from, and what it isn't.</summary>
    private static string Covers(SiteScore score, bool full)
    {
        var covers = score.Covers;
        var all = covers.ChecksRun + covers.ChecksSkipped;
        var checks = covers.ChecksSkipped == 0 ? $"all {all} checks" : $"{covers.ChecksRun} of {all} checks";
        var text = new StringBuilder();
        if (!score.Partial)
        {
            text.Append(covers.Pages == 1 ? "The score covers the 1 page read" : $"The score covers all {Number(covers.Pages)} pages").Append(" and ").Append(checks).Append('.');
        }
        else
        {
            var read = covers.Reason switch
            {
                ScoreIncompleteReason.Stopped => "read before the check was stopped",
                ScoreIncompleteReason.Failed => "read before the check failed",
                ScoreIncompleteReason.UrlLimit => "read before the check reached its limit",
                ScoreIncompleteReason.DepthLimit => "within the check’s limit of clicks from the start",
                _ => "read so far"
            };
            text.Append("The score covers the ").Append(Plural(covers.Pages, "page", "pages")).Append(' ').Append(read).Append(", and ").Append(checks).Append('.');
        }

        if (covers.ChecksSkipped > 0)
        {
            text.Append(' ').Append(covers.ChecksSkipped == 1 ? "1 check couldn’t run" : $"{covers.ChecksSkipped} checks couldn’t run")
                .Append(full ? (covers.ChecksSkipped == 1 ? ": it is listed near the end." : ": they are listed near the end.") : (covers.ChecksSkipped == 1 ? ": the full report lists it." : ": the full report lists them."));
        }

        if (covers.BlockedByRobots > 0)
        {
            text.Append(" robots.txt keeps search engines out of ").Append(Plural(covers.BlockedByRobots, "address", "addresses"))
                .Append(covers.BlockedByRobots == 1 ? ", which wasn’t read." : ", which weren’t read.");
        }

        return text.ToString();
    }

    // ---- 3. Fix these first.

    private static void FixFirst(StringBuilder html, ReportModel model, bool full)
    {
        var score = model.Score;
        var first = model.Problems.Where(problem => problem.Check.Severity != IssueSeverity.Notice).Take(FIRST).ToList();
        if (first.Count == 0 && score is null)
        {
            return;
        }

        if (first.Count == 0)
        {
            var notices = model.ChecksOf(IssueSeverity.Notice);
            html.Append("  <h2>Nothing to fix</h2>\n  <p class=\"lead\">No serious problem was found, and nothing worth fixing.");
            if (notices > 0)
            {
                html.Append(notices == 1 ? " 1 thing is worth a look: it is" : $" {notices} things are worth a look: they are")
                    .Append(full ? " explained under “What to fix”." : " explained in the full report.")
                    .Append(notices == 1 ? " Often it’s fine as it is, and it doesn’t change the score." : " Often they’re fine as they are, and they don’t change the score.");
            }

            html.Append("</p>\n\n");
            return;
        }

        html.Append("  <h2>Fix these first</h2>\n  <ol class=\"first\">\n");
        foreach (var problem in first)
        {
            html.Append("    <li><span class=\"first__n\">").Append(problem.Number).Append("</span>");
            Mark(html, MarkOf(problem.Check.Severity), 14);
            html.Append("<span class=\"first__title\">").AppendText(problem.Words.Title).Append("</span><span class=\"first__count\">")
                .Append(problem.Line is { } line ? CountOf(line, problem.Check) : CountOf(problem.Count, problem.Check)).Append("</span></li>\n");
        }

        html.Append("  </ol>\n  <p class=\"first__foot\">");
        var these = first.Count == 1 ? "this one" : $"these {COUNT_WORDS[first.Count]}";
        if (score is { IfFixed.CheckIds.Count: > 0 } && score.IfFixed.Value != score.Value)
        {
            html.Append("Fixing ").Append(these).Append(" takes the score from ").Append(score.Value).Append(" to ").Append(score.IfFixed.Value).Append(". ");
        }
        else if (score is { IfFixed.CheckIds.Count: > 0 })
        {
            html.Append("Fixing ").Append(these).Append(" leaves the score at ").Append(score.Value).Append(" until the others are fixed too. ");
        }

        // By its section's name, never by a page: which page that starts on depends on the paper and on how long the score's table is.
        html.Append(first.Count == 1 ? "It is" : "Each is")
            .Append(full ? " explained under the same number in “What to fix”, with the first pages it’s on." : " explained in the full report under the same number.")
            .Append("</p>\n\n");
    }

    // ---- 4. How the score is worked out.

    private static void HowTheScoreIsWorkedOut(StringBuilder html, ReportModel model)
    {
        var score = model.Score!;
        // The whole-site figure is said where a fault of the whole site is on the list: its 60 would otherwise have no explanation.
        var siteWide = score.Lines.Any(line => line.Unit == ScoreUnit.Site && line.Severity == IssueSeverity.Error);
        html.Append("  <h2>How the score is worked out</h2>\n  <p class=\"lead\">The score starts at 100, and each check that found a problem takes points off: up to 10 for a serious problem ")
            .Append(siteWide ? "(60 if it is a fault of the whole site) " : string.Empty)
            .Append("and up to 4 for one worth fixing, by how much of the site it affects. Things worth a look take nothing off.</p>\n")
            .Append("  <table class=\"score-table\">\n    <thead><tr><th scope=\"col\">What took points off</th><th scope=\"col\">How much of the site</th><th scope=\"col\" class=\"num\">Points</th></tr></thead>\n    <tbody>\n");
        foreach (var problem in model.Problems.Where(problem => problem.Check.Severity != IssueSeverity.Notice))
        {
            if (problem.Line is not { } line)
            {
                continue;
            }

            html.Append("      <tr><td>");
            Mark(html, MarkOf(problem.Check.Severity), 12);
            html.Append(' ').AppendText(problem.Words.Title).Append("</td><td>").Append(HowMuch(line, problem.Check))
                .Append("</td><td class=\"num\">−").Append(line.Points).Append("</td></tr>\n");
        }

        // What the checks left out of this report took off (hidden, or not among a view's checks): the score is the
        // analysis's whole, so it is one row of its own and the rows add up to the sum below (round 5's review, R2-5).
        var shown = model.Problems.Select(problem => problem.Check.Id).ToHashSet(StringComparer.Ordinal);
        var leftOut = score.Lines.Where(line => line.Points > 0 && !shown.Contains(line.CheckId)).ToList();
        if (leftOut.Count > 0)
        {
            var one = leftOut.Count == 1;
            html.Append("      <tr><td class=\"muted\">")
                .Append(model.Narrowed
                    ? one ? "1 check not in this report" : $"{leftOut.Count} checks not in this report"
                    : one ? "1 hidden check" : $"{leftOut.Count} hidden checks")
                .Append("</td><td class=\"muted\">")
                .Append(model.Narrowed ? string.Empty : "Left out of this report; ")
                .Append(model.Narrowed ? "The score still counts " : "the score still counts ").Append(one ? "it" : "them")
                .Append("</td><td class=\"num\">−").Append(leftOut.Sum(line => line.Points)).Append("</td></tr>\n");
        }

        var notices = model.ChecksOf(IssueSeverity.Notice);
        if (notices > 0)
        {
            html.Append("      <tr><td class=\"muted\">");
            Mark(html, "notice", 12);
            html.Append(' ').Append(Counted(IssueSeverity.Notice, notices)).Append("</td><td class=\"muted\">")
                .Append(notices == 1 ? "It doesn’t change the score" : "They don’t change the score").Append("</td><td class=\"num muted\">0</td></tr>\n");
        }
        else if (score.Lost == 0)
        {
            html.Append("      <tr><td class=\"muted\">Nothing took points off</td><td class=\"muted\"></td><td class=\"num muted\">0</td></tr>\n");
        }

        html.Append("    </tbody>\n    <tfoot><tr><td>Score").Append(score.Partial ? ", so far" : string.Empty).Append("</td><td>100 − ").Append(score.Lost).Append("</td><td class=\"num\">")
            .Append(score.Value).Append("</td></tr></tfoot>\n  </table>\n  <p class=\"score-note\">");
        if (score.HeldBySerious)
        {
            // The serious problem holding it may be one of the checks left out: say so, or the sentence cites a row that isn't there.
            var heldByOneLeftOut = !score.Lines.Any(line => line.Severity == IssueSeverity.Error && line.Points > 0 && shown.Contains(line.CheckId));
            html.Append("Held at ").Append(ScoreCalculator.SERIOUS_CEILING).Append(" while a serious problem stands")
                .Append(heldByOneLeftOut ? ", among the checks left out of this report" : string.Empty)
                .Append(" (").Append(score.BeforeCeiling).Append(" otherwise). ");
        }
        else if (score.Lost > 100)
        {
            html.Append("A score can’t go below 0. ");
        }

        html.Append("A few pages is under 1 in 100; some, under 1 in 10; many, under half; most, half or more. While any serious problem stands, the score can’t be above ")
            .Append(ScoreCalculator.SERIOUS_CEILING).Append(". Score version ").Append(score.Version).Append(".</p>\n\n");
    }

    /// <summary>
    /// How much of the site a line of the score is, which its points follow: "5 of 265 pages: some", "The whole site". A
    /// check reported once that isn't a fault of the whole site has no share to show, and says what it counted as.
    /// </summary>
    private static string HowMuch(ScoreLine line, IssueDefinition check) =>
        line.Unit == ScoreUnit.Site ? "The whole site"
        : check.ReportedOnce ? $"{CHECKED_ONCE} (counts as {Extent(line.Extent)})"
        : $"{CountOf(line, check)}: {Extent(line.Extent)}";

    // ---- 5. What to fix.

    private static void WhatToFix(StringBuilder html, ReportModel model, ReportOptions options)
    {
        if (model.Problems.Count == 0)
        {
            return;
        }

        html.Append("  <div class=\"new-page\">\n  <h2>What to fix</h2>\n  <p class=\"lead\">Every check that found something: what’s wrong, why it matters, how to fix it, and the first pages ")
            .Append("it’s on. The full lists are in the spreadsheet that goes with this report, on the sheet with the same number.</p>\n");
        Group(html, model, options, IssueSeverity.Error, "These stop pages working or being found. Fix them first.");
        Group(html, model, options, IssueSeverity.Warning, "These hold pages back. Fix them when the serious ones are done.");
        Group(html, model, options, IssueSeverity.Notice, "Often fine as they are. They don’t change the score: read each one and decide.");
        html.Append("  </div>\n\n");
    }

    private static void Group(StringBuilder html, ReportModel model, ReportOptions options, IssueSeverity severity, string lead)
    {
        var problems = model.Problems.Where(problem => problem.Check.Severity == severity).ToList();
        if (problems.Count == 0)
        {
            return;
        }

        html.Append("    <h3 class=\"group-title\">");
        Mark(html, MarkOf(severity), 16);
        html.Append(Severity(severity)).Append(" <span>").Append(problems.Count).Append("</span></h3>\n    <p class=\"lead lead--group muted\">").Append(lead).Append("</p>\n");
        foreach (var problem in problems)
        {
            Problem(html, model, problem, severity == IssueSeverity.Notice ? options.LookExamples : options.Examples);
        }
    }

    private static void Problem(StringBuilder html, ReportModel model, ReportProblem problem, int listed)
    {
        // Things worth a look set their text beside their examples, to save paper.
        var compact = problem.Check.Severity == IssueSeverity.Notice;
        var line = problem.Line;
        html.Append(compact ? "    <article class=\"problem problem--compact\">\n      " : "    <article class=\"problem\">\n      <div class=\"keep\">");

        html.Append("<h3>");
        Mark(html, MarkOf(problem.Check.Severity), 14);
        html.Append("<span>").Append(problem.Number).Append(". ").AppendText(problem.Words.Title).Append("</span><span class=\"problem__tech\">").AppendText(problem.Check.Title)
            .Append(" · ").Append(Technical(problem.Check.Severity)).Append("</span></h3>\n      <p class=\"problem__meta\"><b>")
            .Append(line is null ? CountOf(problem.Count, problem.Check) : CountOf(line, problem.Check)).Append("</b>");
        if (line is not null)
        {
            html.Append(" · ").Append(line.Points == 0 ? "doesn’t change the score" : line.Points == 1 ? "takes 1 point off the score" : $"takes {line.Points} points off the score");
        }

        if (problem.Words.Fixer is { } fixer)
        {
            html.Append(" · usually fixed by ").Append(FixedBy(fixer));
        }

        html.Append("</p>\n");
        if (compact)
        {
            html.Append("      <div class=\"problem__split\"><div>");
        }

        html.Append("<h4>What’s wrong, and why it matters</h4>\n        ");
        if (problem.Words.IsPlain)
        {
            html.Append("<p>").AppendText(string.Join(' ', problem.Words.What)).Append("</p>");
        }
        else
        {
            foreach (var paragraph in problem.Words.What)
            {
                html.Append("<p>").AppendText(paragraph).Append("</p>");
            }
        }

        html.Append("\n        <h4>How to fix it</h4>\n        ");
        if (problem.Words.Fix.Count == 1)
        {
            // One step needs no number.
            html.Append("<p>").AppendText(problem.Words.Fix[0]).Append("</p>");
        }
        else
        {
            html.Append("<ol class=\"steps\">");
            foreach (var step in problem.Words.Fix)
            {
                html.Append("<li><span>").AppendText(step).Append("</span></li>");
            }

            html.Append("</ol>");
        }

        OneFix(html, model, problem);
        html.Append(compact ? "</div><div>" : "</div>\n      <div class=\"keep\">");
        Examples(html, model, problem, listed, compact);
        html.Append(compact ? "</div></div>\n    </article>\n" : "</div>\n    </article>\n");
    }

    /// <summary>
    /// "One fix clears 264 of these 265 pages", with what they share. A check of addresses counts addresses, which lead
    /// to the address they share (a chain of forwards) where a page names it (a link, a canonical).
    /// </summary>
    private static void OneFix(StringBuilder html, ReportModel model, ReportProblem problem)
    {
        if (problem.OneFix is not { } fix)
        {
            return;
        }

        var ofAddresses = problem.Check.Subject == IssueSubject.Url;
        html.Append("<p class=\"onefix\"><b>One fix clears ").Append(Number(fix.Pages)).Append(" of these ")
            .Append(ofAddresses ? Plural(problem.Count, "address", "addresses") : Plural(problem.Count, "page", "pages")).Append(".</b> ");
        var usually = problem.Words.OneFix;
        if (fix.RelatedUrl is { } shared)
        {
            html.Append(ofAddresses ? "They all lead to the same address, " : "They all name the same address, ");
            Url(html, model, shared);
            html.Append(usually is null ? ", so the fault is most likely in the part of the site every page shares." : ".");
        }
        else if (fix.Detail is { } fault)
        {
            html.Append("They all have the same fault, “").AppendText(HtmlText.Cut(fault, SHOWN_DETAIL))
                .Append(usually is null ? "”, so it is most likely in the part of the site every page shares." : "”.");
        }
        else
        {
            html.Append(usually is null ? "They share one cause, most likely in the part of the site every page shares." : "They share one cause.");
        }

        if (usually is not null)
        {
            html.Append(' ').AppendText(usually);
        }

        html.Append("</p>");
    }

    private static void Examples(StringBuilder html, ReportModel model, ReportProblem problem, int listed, bool compact)
    {
        var rows = problem.Examples.Take(listed).ToList();
        var more = problem.Count - rows.Count;
        if (rows.Count == 0)
        {
            html.Append("<p class=\"more\">The full list is on sheet ").Append(problem.Number).Append(" of the spreadsheet.</p>");
            return;
        }

        var caption = more > 0 ? $"The first {rows.Count} of {Number(problem.Count)}" : problem.Count == 1 ? "Where it is" : $"All {problem.Count}";
        var isUrl = problem.Check.Subject == IssueSubject.Url;
        var whereFound = problem.Check.References == IssueReferences.Sources;
        html.Append(compact ? "<table class=\"examples examples--one\">" : whereFound ? "<table class=\"examples examples--where\">" : "<table class=\"examples\">")
            .Append("\n          <caption>").Append(caption).Append("</caption>\n");
        if (!compact)
        {
            html.Append("          <thead><tr><th scope=\"col\">").Append(isUrl ? "Address" : "Page").Append("</th><th scope=\"col\">What was found</th>")
                .Append(whereFound ? "<th scope=\"col\">Where to fix it</th>" : string.Empty).Append("</tr></thead>\n");
        }

        html.Append("          <tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr><td>");
            Url(html, model, row.Url);
            if (compact)
            {
                if (row.Detail is not null || row.RelatedUrl is not null)
                {
                    html.Append("<span class=\"found\">");
                    Found(html, model, row);
                    html.Append("</span>");
                }

                if (row.FoundOn is { } foundOn)
                {
                    html.Append("<span class=\"found\">");
                    WhereFound(html, model, foundOn);
                    html.Append("</span>");
                }

                html.Append("</td></tr>");
            }
            else
            {
                html.Append("</td><td>");
                Found(html, model, row);
                if (whereFound)
                {
                    html.Append("</td><td>");
                    if (row.FoundOn is { } foundOn)
                    {
                        WhereFound(html, model, foundOn);
                    }
                }

                html.Append("</td></tr>");
            }
        }

        html.Append("</tbody>\n        </table>");
        if (more > 0)
        {
            html.Append("<p class=\"more\">And ").Append(Number(more)).Append(" more: the full list is on sheet ").Append(problem.Number).Append(" of the spreadsheet.</p>");
        }
    }

    /// <summary>What was found on a page, and the other URL it's about.</summary>
    private static void Found(StringBuilder html, ReportModel model, ReportExample row)
    {
        if (row.Detail is { } detail)
        {
            html.AppendText(HtmlText.Cut(detail, SHOWN_DETAIL));
        }

        if (row.RelatedUrl is { } related)
        {
            html.Append(row.Detail is null ? "→ " : " → ");
            Url(html, model, related);
            if (row.Targets is > 1 and var targets)
            {
                html.Append(" and ").Append(Number(targets - 1)).Append(targets == 2 ? " other" : " others");
            }
        }
    }

    /// <summary>
    /// Where a URL other pages refer to is found, so a reader knows what to change: the pages linking to it (the first few,
    /// then how many more), the sitemaps listing it, or that it's the address the check started from.
    /// </summary>
    private static void WhereFound(StringBuilder html, ReportModel model, UrlFoundOn foundOn)
    {
        List<string> pages = [.. foundOn.Sources.Select(source => source.Url).Distinct(StringComparer.Ordinal)];
        List<Action> parts = [];
        if (foundOn.Pages > 0)
        {
            parts.Add(() =>
            {
                html.Append("Linked from ").Append(foundOn.Pages == 1 ? "1 page" : $"{Number(foundOn.Pages)}{(foundOn.PagesExact ? string.Empty : "+")} pages").Append(": ");
                for (var index = 0; index < pages.Count; index++)
                {
                    html.Append(index == 0 ? string.Empty : ", ");
                    Url(html, model, pages[index]);
                }

                if (foundOn.Pages > pages.Count)
                {
                    html.Append(" and ").Append(Number(foundOn.Pages - pages.Count)).Append(" more");
                }
            });
        }

        if (foundOn.Sitemaps.Total > 0)
        {
            parts.Add(() =>
            {
                html.Append(foundOn.Sitemaps.Total == 1 ? "Listed in the sitemap " : $"Listed in {Number(foundOn.Sitemaps.Total)} sitemaps, first ");
                Url(html, model, foundOn.Sitemaps.Sitemaps[0].Url);
            });
        }

        if (foundOn.Start)
        {
            parts.Add(() => html.Append("The address the check started from"));
        }

        if (parts.Count == 0)
        {
            html.Append("No page read links to it");
            return;
        }

        for (var index = 0; index < parts.Count; index++)
        {
            html.Append(index == 0 ? string.Empty : ". ");
            parts[index]();
        }
    }

    /// <summary>
    /// A URL as the report shows one: without the site's own origin, cut to a length, and a link only when it's http or
    /// https. The link opens apart from the document, and tells the site nothing of where it came from.
    /// </summary>
    private static void Url(StringBuilder html, ReportModel model, string url)
    {
        var shown = url.StartsWith(model.Origin, StringComparison.Ordinal) && (url.Length == model.Origin.Length || url[model.Origin.Length] is '/' or '?' or '#')
            ? url.Length == model.Origin.Length ? "/" : url[model.Origin.Length..]
            : url;
        html.Append("<span class=\"mono\">");
        if (HtmlText.Linkable(url) is { } href)
        {
            html.Append("<a href=\"").AppendText(href).Append("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">").AppendText(HtmlText.Cut(shown, SHOWN_URL)).Append("</a>");
        }
        else
        {
            html.AppendText(HtmlText.Cut(shown, SHOWN_URL));
        }

        html.Append("</span>");
    }

    // ---- 6. What couldn't be checked.

    private static void CouldNotBeChecked(StringBuilder html, ReportModel model)
    {
        if (model.Skipped.Count == 0)
        {
            return;
        }

        html.Append("  <h2>What couldn’t be checked</h2>\n  <p class=\"lead\">").Append(model.Skipped.Count == 1 ? "This check didn’t run" : "These checks didn’t run")
            .Append(", so the site may have problems ").Append(model.Skipped.Count == 1 ? "it" : "they").Append(" would have found.</p>\n  <ul class=\"plain-list\">\n");
        foreach (var skipped in model.Skipped)
        {
            html.Append("    <li>");
            Mark(html, "skipped", 14);
            html.Append("<span><b>").AppendText(skipped.Words.Named).Append(".</b> ").AppendText(skipped.Why);
            if (skipped.Action is { } action)
            {
                html.Append(' ').AppendText(action);
            }

            html.Append("</span></li>\n");
        }

        html.Append("  </ul>\n\n");
    }

    // ---- 7. What was checked and found nothing wrong.

    private static void FoundNothingWrong(StringBuilder html, ReportModel model)
    {
        if (model.Passed.Count == 0)
        {
            return;
        }

        html.Append("  <h2>What was checked and found nothing wrong</h2>\n  <p class=\"lead\">")
            .Append(model.Passed.Count == 1 ? "1 check ran and found nothing wrong." : $"{model.Passed.Count} checks ran and found nothing wrong.").Append("</p>\n");

        // Two columns of categories, a pair to a row, so a page can break between rows.
        var categories = model.Passed.GroupBy(passed => passed.Check.Category).ToList();
        for (var index = 0; index < categories.Count; index += 2)
        {
            html.Append("  <div class=\"passed\">");
            foreach (var category in categories.Skip(index).Take(2))
            {
                html.Append("<div><h4>").AppendText(ReportSources.Category(category.Key)).Append("</h4><ul>");
                foreach (var passed in category)
                {
                    html.Append("<li>");
                    Mark(html, "passed", 12);
                    html.Append("<span>").AppendText(passed.Words.Passed).Append("</span></li>");
                }

                html.Append("</ul></div>");
            }

            html.Append(index + 1 < categories.Count ? "</div>\n" : "<div></div></div>\n");
        }

        html.Append('\n');
    }

    // ---- 8. About this check.

    private static void AboutThisCheck(StringBuilder html, ReportModel model)
    {
        var zone = model.TimeZone;
        html.Append("  <h2>About this check</h2>\n  <dl class=\"facts\">\n    <dt>Started from</dt><dd class=\"mono\">").AppendText(HtmlText.Cut(model.StartUrl, SHOWN_URL)).Append("</dd>\n");

        html.Append("    <dt>When</dt><dd>").Append(Date(model.StartedAt, zone)).Append(", ");
        if (model.FinishedAt is { } finished)
        {
            var sameDay = TimeZoneInfo.ConvertTime(model.StartedAt, zone).Date == TimeZoneInfo.ConvertTime(finished, zone).Date;
            html.Append(Time(model.StartedAt, zone)).Append(" to ").Append(sameDay ? string.Empty : Date(finished, zone) + ", ").Append(Time(finished, zone));
            if (model.TimeCrawling is { } crawling)
            {
                html.Append(" (").Append(Duration(crawling)).Append(finished - model.StartedAt > crawling + TimeSpan.FromMinutes(1) ? " spent checking" : string.Empty).Append(')');
            }
            else
            {
                html.Append(" (").Append(Duration(finished - model.StartedAt)).Append(')');
            }
        }
        else
        {
            html.Append("from ").Append(Time(model.StartedAt, zone));
        }

        html.Append("</dd>\n    <dt>What was read</dt><dd>").Append(Plural(model.Pages, "page", "pages")).Append(", and ")
            .Append(Number(Math.Max(0, model.Urls - model.Pages))).Append(" images, files and links: ").Append(Plural(model.Urls, "address", "addresses")).Append(" in all</dd>\n");

        // What follows is read from the crawl as it stands. Once the crawl has gone on, that is another crawl than the
        // one the results are of ("4,064 of 3,113 pages"): it is left out, and the note under the site says why.
        if (!model.CrawlWentOn)
        {
            html.Append("    <dt>Pages that can appear in search</dt><dd>").Append(Number(model.IndexablePages)).Append(" of ").Append(Number(model.Pages)).Append("</dd>\n");
            var notRead = NotRead(model);
            if (notRead.Count > 0)
            {
                html.Append("    <dt>Not read</dt><dd>").Append(string.Join("; ", notRead)).Append("</dd>\n");
            }
        }

        html.Append("    <dt>How it was read</dt><dd>").Append(HowItWasRead(model.Options)).Append("</dd>\n")
            .Append("    <dt>Finished?</dt><dd>")
            .Append(model.CrawlWentOn ? "Not known from these results: the check has gone on since they were worked out."
                : model.Finished ? "Yes: it read every address it found"
                : "No. " + Unfinished(model) + HowToFinish(model))
            .Append("</dd>\n")
            .Append("    <dt>Results worked out</dt><dd>").Append(Date(model.AnalyzedAt, zone)).Append(", ").Append(Time(model.AnalyzedAt, zone)).Append("</dd>\n")
            .Append("    <dt>The words in this report</dt><dd>Serious is the checks’ “Error”, Worth fixing is “Warning” and Worth a look is “Notice”. An address is a URL.</dd>\n")
            .Append("  </dl>\n\n");
    }

    /// <summary>What a crawl that didn't finish needs, after how it stands.</summary>
    private static string HowToFinish(ReportModel model) => model.Status switch
    {
        CrawlStatus.Completed => " Check the site again with a higher limit to read the rest.",
        CrawlStatus.Stopped or CrawlStatus.Failed => " Continue the check to read the rest.",
        _ => string.Empty
    };

    /// <summary>The addresses found and not read, a phrase for each reason.</summary>
    private static List<string> NotRead(ReportModel model)
    {
        List<string> phrases = [];
        void Add(SkipReason reason, Func<long, string> phrase)
        {
            if (model.NotRead.GetValueOrDefault(reason) is > 0 and var count)
            {
                phrases.Add(phrase(count));
            }
        }

        Add(SkipReason.NoFollow, count => $"{Plural(count, "address", "addresses")} the site asks search engines not to follow (“nofollow”)");
        Add(SkipReason.UrlLimit, count => $"{Plural(count, "address", "addresses")} past the check’s limit");
        Add(SkipReason.DepthLimit, count => $"{Plural(count, "address", "addresses")} more clicks from the start than the check’s limit");
        Add(SkipReason.ExternalsDisabled, count => $"{Plural(count, "address", "addresses")} on other websites, which this check was set to leave");
        Add(SkipReason.ResourcesDisabled, count => $"{Plural(count, "image or file", "images and files")}, which this check was set to leave");
        Add(SkipReason.NotFollowedByOption, count => $"{Plural(count, "address", "addresses")} of a kind this check was set not to follow");
        Add(SkipReason.None, count => $"{Plural(count, "address", "addresses")} found and not read");
        if (model.BlockedByRobots > 0)
        {
            phrases.Add($"{Plural(model.BlockedByRobots, "address", "addresses")} the site’s rules file (robots.txt) keeps search engines out of");
        }

        return phrases;
    }

    private static string HowItWasRead(CrawlOptions options)
    {
        var speed = options.MaxRequestsPerSecond > 0
            ? $"Up to {options.MaxRequestsPerSecond.ToString("0.##", CultureInfo.InvariantCulture)} {(options.MaxRequestsPerSecond == 1 ? "address" : "addresses")} a second"
            : $"Up to {options.Concurrency} addresses at a time";
        var limit = options.MaxUrls > 0 ? $"stopping after {Plural(options.MaxUrls, "address", "addresses")}" : "with no limit on addresses";
        return $"{speed}, {limit}, {(options.RespectRobotsTxt ? "obeying" : "ignoring")} the site’s rules file (robots.txt)";
    }

    // ---- The marks.

    private static string MarkOf(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => "error",
        IssueSeverity.Warning => "warning",
        _ => "notice"
    };

    /// <summary>A severity's shape, or the passed and skipped marks, as inline SVG in the light colours: the report has no theme.</summary>
    private static void Mark(StringBuilder html, string mark, int size)
    {
        var (label, body, outline) = mark switch
        {
            "error" => ("Serious",
                "<path d=\"M8.1 1.5h7.8l5.6 5.6v7.8l-5.6 5.6H8.1l-5.6-5.6V7.1z\" fill=\"#D93636\" stroke=\"none\"/><path d=\"M12 7v6\" stroke=\"#FFFFFF\" stroke-width=\"2.4\"/><circle cx=\"12\" cy=\"16.6\" r=\"1.35\" fill=\"#FFFFFF\" stroke=\"none\"/>",
                false),
            "warning" => ("Worth fixing",
                "<path d=\"M12 2.4 22.4 20.4H1.6z\" fill=\"#F5A70A\" stroke=\"#B06C00\" stroke-width=\"1.4\" stroke-linejoin=\"round\"/><path d=\"M12 9v5.2\" stroke=\"#3A2600\" stroke-width=\"2.3\"/><circle cx=\"12\" cy=\"17.2\" r=\"1.25\" fill=\"#3A2600\" stroke=\"none\"/>",
                false),
            "notice" => ("Worth a look",
                "<circle cx=\"12\" cy=\"12\" r=\"10.4\" fill=\"#5F6B79\" stroke=\"none\"/><path d=\"M12 10.6v6.2\" stroke=\"#FFFFFF\" stroke-width=\"2.4\"/><circle cx=\"12\" cy=\"7.3\" r=\"1.4\" fill=\"#FFFFFF\" stroke=\"none\"/>",
                false),
            "passed" => ("Nothing wrong", "<circle cx=\"12\" cy=\"12\" r=\"9.5\"/><path d=\"m8.5 12.2 2.4 2.4 4.6-4.8\"/>", true),
            _ => ("Couldn’t check", "<circle cx=\"12\" cy=\"12\" r=\"9.5\" stroke-dasharray=\"3.2 2.6\"/><path d=\"M8.5 12h7\"/>", true)
        };
        html.Append("<svg class=\"mark\" width=\"").Append(size).Append("\" height=\"").Append(size).Append("\" viewBox=\"0 0 24 24\" role=\"img\" aria-label=\"").Append(label)
            .Append(outline ? "\" fill=\"none\" stroke=\"#68686F\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" : "\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">")
            .Append(body).Append("</svg>");
    }

    /// <summary>
    /// The score as a ring: an arc of the accent blue on a neutral track, the number inside. Never a severity's colour. A
    /// crawl that didn't finish has a dashed arc and "so far"; a crawl with no score has the track alone, and so has a
    /// score of 0, whose arc of no length a round end would draw as a dot.
    /// </summary>
    private static void Ring(StringBuilder html, SiteScore? score)
    {
        var circumference = 2 * Math.PI * RING_RADIUS;
        static string Fixed(double number) => number.ToString("0.#", CultureInfo.InvariantCulture);
        var label = score is null ? "No score" : $"Score {score.Value} out of 100{(score.Partial ? ", so far" : string.Empty)}";
        html.Append("<svg class=\"ring\" width=\"112\" height=\"112\" viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"").Append(label).Append("\">")
            .Append("<circle cx=\"50\" cy=\"50\" r=\"44\" fill=\"none\" stroke=\"#EEEEF1\" stroke-width=\"7\"/>");
        if (score is { Partial: false, Value: > 0 })
        {
            html.Append("<circle cx=\"50\" cy=\"50\" r=\"44\" fill=\"none\" stroke=\"#1F86F5\" stroke-width=\"7\" stroke-linecap=\"round\" stroke-dasharray=\"")
                .Append(Fixed(circumference * score.Value / 100)).Append(' ').Append(Fixed(circumference)).Append("\" transform=\"rotate(-90 50 50)\"/>");
        }
        else if (score is { Value: > 0 })
        {
            // Dashed, and only as long as the score: an arc from the top, clockwise.
            var angle = 2 * Math.PI * Math.Min(score.Value, 99.9) / 100;
            html.Append("<path d=\"M50 6 A44 44 0 ").Append(score.Value > 50 ? '1' : '0').Append(" 1 ")
                .Append(Fixed(50 + RING_RADIUS * Math.Sin(angle))).Append(' ').Append(Fixed(50 - RING_RADIUS * Math.Cos(angle)))
                .Append("\" fill=\"none\" stroke=\"#1F86F5\" stroke-width=\"7\" stroke-dasharray=\"3.2 2.6\"/>");
        }

        html.Append("<text x=\"50\" y=\"52\" text-anchor=\"middle\" dominant-baseline=\"middle\" font-size=\"34\" font-weight=\"500\" fill=\"#18181B\" letter-spacing=\"-1\">")
            .Append(score is null ? "–" : score.Value.ToString(CultureInfo.InvariantCulture)).Append("</text>")
            .Append("<text x=\"50\" y=\"72\" text-anchor=\"middle\" font-size=\"8.5\" fill=\"#68686F\">").Append(score is null ? "no score" : score.Partial ? "so far" : "out of 100")
            .Append("</text></svg>");
    }
}
