namespace ShoutingIguana.Engine.Storage.Models;

/// <param name="ParkedLinked">
/// Linked URLs in flight that are parked for a long while (a section the site limits, read one page a minute): they don't
/// hold up the URLs only sitemaps list, as other linked URLs in flight do.
/// </param>
internal sealed record ClaimCommand(int InternalCount, int ExternalCount, TaskCompletionSource<FrontierClaim> Reply, int ParkedLinked = 0) : WriterCommand;
