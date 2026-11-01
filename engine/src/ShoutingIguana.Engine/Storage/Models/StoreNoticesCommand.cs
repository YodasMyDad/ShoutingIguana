namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>Wakes the writer to store the notices waiting for it. The notices themselves wait in a queue, so none is lost when the channel is full.</summary>
internal sealed record StoreNoticesCommand : WriterCommand
{
    public static StoreNoticesCommand Instance { get; } = new();
}
