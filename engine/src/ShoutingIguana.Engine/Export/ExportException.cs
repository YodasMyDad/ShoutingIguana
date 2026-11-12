using ShoutingIguana.Engine.Export.Models;

namespace ShoutingIguana.Engine.Export;

/// <summary>An export that wrote no file, with why. Its message is a plain sentence for whoever asked for the export.</summary>
public sealed class ExportException(ExportFailure failure, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public ExportFailure Failure { get; } = failure;

    /// <summary>For <see cref="ExportFailure.InvalidRequest"/>: the field of the request that's wrong, when it is one field's.</summary>
    public string? Field { get; init; }
}
