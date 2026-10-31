namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record RecordPageCommand(PageRecord Record) : WriterCommand;
