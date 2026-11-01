namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A crawl or an analysis, in this process or another, holds the crawl file's lock, so nothing else may write to it now;
/// or someone is part-way through reading a closed crawl file, which keeps writers out until they finish. Its message is
/// written for users. Still an <see cref="InvalidOperationException"/>, as every refusal to open a crawl file is, so
/// hosts that show those messages need nothing new; the type lets one tell "busy" from "not a crawl".
/// </summary>
public sealed class CrawlFileInUseException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
