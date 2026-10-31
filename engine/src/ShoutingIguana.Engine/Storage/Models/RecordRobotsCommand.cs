namespace ShoutingIguana.Engine.Storage.Models;

internal sealed record RecordRobotsCommand(RobotsRecord Record) : WriterCommand;
