using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ShoutingIguana.Core.Repositories;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Helper for transforming custom extraction findings into a pivoted, per-page dataset.
/// </summary>
public static class CustomExtractionPivotBuilder
{
    private const int BatchSize = 1000;

    public record PivotColumn(string Key, string DisplayName);
    public record PivotRow(string Page, Severity Severity, IReadOnlyDictionary<string, string> Values);
    public record PivotResult(IReadOnlyList<PivotColumn> Columns, IReadOnlyList<PivotRow> Rows);

    /// <summary>
    /// Builds a pivoted result set for the custom extraction plugin.
    /// </summary>
    public static async Task<PivotResult> BuildAsync(
        int projectId,
        string taskKey,
        IReportDataRepository reportRepository,
        ICustomExtractionRuleRepository ruleRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportRepository);
        ArgumentNullException.ThrowIfNull(ruleRepository);

        var baseColumns = await LoadRuleColumnsAsync(ruleRepository, projectId, cancellationToken).ConfigureAwait(false);
        var rows = await LoadAllReportRowsAsync(reportRepository, projectId, taskKey, cancellationToken).ConfigureAwait(false);

        return AggregateRows(rows, baseColumns);
    }

    private static async Task<List<PivotColumn>> LoadRuleColumnsAsync(
        ICustomExtractionRuleRepository ruleRepository,
        int projectId,
        CancellationToken cancellationToken)
    {
        var rules = await ruleRepository.GetByProjectIdAsync(projectId).ConfigureAwait(false);

        return rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new PivotColumn(r.Name, r.Name))
            .ToList();
    }

    private static async Task<List<Core.Models.ReportRow>> LoadAllReportRowsAsync(
        IReportDataRepository reportRepository,
        int projectId,
        string taskKey,
        CancellationToken cancellationToken)
    {
        var results = new List<Core.Models.ReportRow>();
        var page = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await reportRepository
                .GetByTaskKeyAsync(projectId, taskKey, page, BatchSize)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            results.AddRange(batch);

            if (batch.Count < BatchSize)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private static PivotResult AggregateRows(
        IEnumerable<Core.Models.ReportRow> rows,
        List<PivotColumn> seedColumns)
    {
        var columns = new List<PivotColumn>(seedColumns);
        var columnNames = new HashSet<string>(columns.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
        var aggregated = new Dictionary<string, AggregatedPageRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var reportRow in rows)
        {
            var data = reportRow.GetData();
            if (data == null)
            {
                continue;
            }

            var page = GetString(data, "Page");
            if (string.IsNullOrWhiteSpace(page))
            {
                continue;
            }

            var entry = aggregated.GetValueOrDefault(page) ?? CreatePageRow(aggregated, page);

            entry.Id ??= reportRow.Id;
            entry.UpdateSeverity(GetSeverity(data));

            var ruleName = GetString(data, "RuleName");
            var extractedValues = GetValues(data);
            if (!string.IsNullOrWhiteSpace(ruleName))
            {
                entry.AddValues(ruleName, extractedValues);

                if (columnNames.Add(ruleName))
                {
                    columns.Add(new PivotColumn(ruleName, ruleName));
                }
            }
        }

        var orderedRows = aggregated.Values
            .OrderBy(r => r.Page, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var column in columns)
                {
                    r.Values.TryGetValue(column.Key, out var columnValues);
                    values[column.Key] = FormatCellValue(columnValues);
                }

                return new PivotRow(r.Page, r.Severity, values);
            })
            .ToList();

        return new PivotResult(columns, orderedRows);
    }

    private static AggregatedPageRow CreatePageRow(Dictionary<string, AggregatedPageRow> map, string page)
    {
        var entry = new AggregatedPageRow(page);
        map[page] = entry;
        return entry;
    }

    private static string? GetString(Dictionary<string, object?> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static IEnumerable<string> GetValues(Dictionary<string, object?> data)
    {
        if (data.TryGetValue("ExtractedValuesRaw", out var raw) && raw is object[] array)
        {
            foreach (var entry in array)
            {
                var text = entry?.ToString();
                var decoded = Decode(text);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    yield return decoded;
                }
            }
            yield break;
        }

        var fallback = GetString(data, "ExtractedValue");
        var decodedFallback = Decode(fallback);
        if (!string.IsNullOrWhiteSpace(decodedFallback))
        {
            yield return decodedFallback;
        }
    }

    private static Severity GetSeverity(Dictionary<string, object?> data)
    {
        if (data.TryGetValue("Severity", out var value) && value != null)
        {
            if (value is Severity severity)
            {
                return severity;
            }

            if (Enum.TryParse(value.ToString(), true, out Severity parsed))
            {
                return parsed;
            }
        }

        return Severity.Info;
    }

    private static string FormatCellValue(List<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            return string.Empty;
        }

        var cleaned = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());

        return string.Join(Environment.NewLine, cleaned);
    }

    private static string? Decode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? value : WebUtility.HtmlDecode(value);
    }

    private sealed class AggregatedPageRow
    {
        private static readonly Dictionary<Severity, int> SeverityPriority = new()
        {
            [Severity.Error] = 0,
            [Severity.Warning] = 1,
            [Severity.Info] = 2
        };

        public AggregatedPageRow(string page)
        {
            Page = page;
        }

        public int? Id { get; set; }
        public string Page { get; }
        public Severity Severity { get; private set; } = Severity.Info;
        public Dictionary<string, List<string>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void UpdateSeverity(Severity incoming)
        {
            if (SeverityPriority[incoming] < SeverityPriority[Severity])
            {
                Severity = incoming;
            }
        }

        public void AddValues(string ruleName, IEnumerable<string> values)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
            {
                return;
            }

            if (!Values.TryGetValue(ruleName, out var list))
            {
                list = [];
                Values[ruleName] = list;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    list.Add(value.Trim());
                }
            }
        }
    }
}
