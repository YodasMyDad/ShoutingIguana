namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record RecordFaviconCommand(FaviconRecord Record) : WriterCommand;
