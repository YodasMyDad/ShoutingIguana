namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>A recheck's first phase is over: hand out the second's URLs, and reply how many there are (0 when it has none).</summary>
internal sealed record NextRecheckPhaseCommand(TaskCompletionSource<int> Reply) : WriterCommand;
