namespace ShoutingIguana.Cli.Benchmarking.Models;

internal sealed record MemorySample(double Seconds, long Fetched, long WorkingSetBytes, long ManagedBytes);
