namespace ShoutingIguana.Sidecar.Hosting;

/// <summary>Another sidecar is using the state folder: the program says so and exits with its own code.</summary>
internal sealed class StateFolderInUseException(string message, Exception innerException) : IOException(message, innerException);
