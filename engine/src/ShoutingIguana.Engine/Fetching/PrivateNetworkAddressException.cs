namespace ShoutingIguana.Engine.Fetching;

/// <summary>A host of a public site's crawl resolves only to private addresses, so the crawler won't connect to it.</summary>
internal sealed class PrivateNetworkAddressException(string host)
    : Exception($"{host} resolves to a private network address, which a crawl of a public site doesn't request");
