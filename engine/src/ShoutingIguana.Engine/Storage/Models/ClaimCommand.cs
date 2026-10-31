namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record ClaimCommand(int InternalCount, int ExternalCount, TaskCompletionSource<FrontierClaim> Reply) : WriterCommand;
