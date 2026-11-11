S = 'src/ShoutingIguana.Sidecar/'
T = 'tests/ShoutingIguana.Sidecar.Tests/AnalysisSettingsTests.cs'

EDITS = [
(S + 'Options/AnalysisLimits.cs',
'''    public static AnalysisLimit? Find(string id) => All.FirstOrDefault(limit => limit.Id == id);
''',
'''    public static AnalysisLimit? Find(string id) => All.FirstOrDefault(limit => limit.Id == id);

    /// <summary>The other limit of a pair (a shortest length and its longest), or null for a limit that stands alone.</summary>
    public static string? PartnerOf(string id) =>
        PAIRS.Where(pair => pair.Min == id || pair.Max == id).Select(pair => pair.Min == id ? pair.Max : pair.Min).FirstOrDefault();
'''),
(S + 'Options/AnalysisSettingsStore.cs',
'''            var kept = AnalysisLimits.Values(_current).Where(value => !limits.Contains(value.Key)).ToDictionary(StringComparer.Ordinal);
            var settings = AnalysisLimits.Apply(kept, out var broken);
            foreach (var field in broken.Keys)
            {
                kept.Remove(field);
            }

            return Store(broken.Count == 0 ? settings : AnalysisLimits.Apply(kept, out _));
''',
'''            var kept = AnalysisLimits.Values(_current).Where(value => !limits.Contains(value.Key)).ToDictionary(StringComparer.Ordinal);
            var settings = AnalysisLimits.Apply(kept, out var broken);
            foreach (var field in broken.Keys)
            {
                kept.Remove(field);
                kept.Remove(AnalysisLimits.PartnerOf(field) ?? field);
            }

            return Store(broken.Count == 0 ? settings : AnalysisLimits.Apply(kept, out _));
'''),
(S + 'Crawls/CrawlManager.cs',
'''    /// <summary>Tests only: called with the crawl's id once <see cref="Alerts"/> has read the file, before it adds the unstored ones.</summary>
''',
'''    /// <summary>Tests only: called with the crawl's id as its analysis is about to run, the limits it runs with already taken.</summary>
    internal Action<string>? AnalysisStarting { get; set; }

    /// <summary>Tests only: called with the crawl's id once <see cref="Alerts"/> has read the file, before it adds the unstored ones.</summary>
'''),
(S + 'Crawls/CrawlManager.cs',
'''            var settings = AnalysisSettings;
            var summary = await new CrawlAnalyzer(loggerFactory)
''',
'''            var settings = AnalysisSettings;
            AnalysisStarting?.Invoke(crawl.Id);
            var summary = await new CrawlAnalyzer(loggerFactory)
'''),
(T,
'''        var changed = 0;
        manager.Publishing = published =>
        {
            // The analysis has begun and says how far it is: the limits change under it.
            if (published is AnalysisEventDto progress && progress.CrawlId == id && Interlocked.Exchange(ref changed, 1) == 0)
            {
                store.Save(new Dictionary<string, JsonElement> { ["titleMaxLength"] = JsonSerializer.SerializeToElement(12) });
            }
        };
''',
'''        var changed = 0;
        manager.AnalysisStarting = starting =>
        {
            // The analysis has taken its limits and is about to run: they change under it.
            if (starting == id && Interlocked.Exchange(ref changed, 1) == 0)
            {
                store.Save(new Dictionary<string, JsonElement> { ["titleMinLength"] = JsonSerializer.SerializeToElement(5), ["titleMaxLength"] = JsonSerializer.SerializeToElement(12) });
            }
        };
'''),
(T,
'''        finally
        {
            manager.Publishing = null;
        }
''',
'''        finally
        {
            manager.AnalysisStarting = null;
        }
'''),
(T,
'''        Assert.Equal(HttpStatusCode.OK, (await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMaxLength":12}"""))).Status);
''',
'''        Assert.Equal(HttpStatusCode.OK, (await _sidecar.SendAsync(HttpMethod.Put, PATH, JsonNode.Parse("""{"titleMinLength":5,"titleMaxLength":12}"""))).Status);
'''),
(T,
'''using ShoutingIguana.Sidecar.Crawls;
using ShoutingIguana.Sidecar.Events.Models;
''',
'''using ShoutingIguana.Sidecar.Crawls;
'''),
]
