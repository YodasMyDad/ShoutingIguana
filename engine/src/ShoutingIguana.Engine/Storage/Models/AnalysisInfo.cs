namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>When and by which engine a crawl was last analysed.</summary>
public sealed record AnalysisInfo(DateTimeOffset AnalyzedAt, string EngineVersion, long PagesAnalyzed, TimeSpan Duration);
