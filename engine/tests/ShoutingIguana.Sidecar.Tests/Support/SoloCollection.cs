namespace ShoutingIguana.Sidecar.Tests.Support;

/// <summary>Tests that run with nothing else running, as they measure the process: what it allocates, how it keeps up.</summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class SoloCollection
{
    public const string NAME = "Solo";
}
