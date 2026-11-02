using System.Text.Json.Serialization;
using ShoutingIguana.Cli.Benchmarking.Models;

namespace ShoutingIguana.Cli.Benchmarking;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BenchResult))]
[JsonSerializable(typeof(ReaderBenchResult))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}
