using System.Text.Json.Serialization;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Engine.Configuration.Models;
using ShoutingIguana.Engine.Crawling.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Serialization;

/// <summary>Source-generated serialisers: no reflection at runtime, and trimming/AOT safe.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ResponseDetails))]
[JsonSerializable(typeof(CrawlOptions))]
[JsonSerializable(typeof(AnalysisSettings))]
[JsonSerializable(typeof(CrawlNoticeData))]
internal sealed partial class EngineJsonContext : JsonSerializerContext
{
}
