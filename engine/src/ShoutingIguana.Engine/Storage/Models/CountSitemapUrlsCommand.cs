namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record CountSitemapUrlsCommand(TaskCompletionSource<long> Reply) : WriterCommand;
