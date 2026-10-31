namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record RegisterSitemapCommand(string Url, int? ParentId, TaskCompletionSource<int> Reply) : WriterCommand;
