namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record CompleteSitemapCommand(SitemapRecord Record) : WriterCommand;
