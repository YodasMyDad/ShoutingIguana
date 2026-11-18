namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>What the crawl's signatures had to do with a request (<see cref="SignatureBook.Explain"/>).</summary>
public enum SignatureSituationKind
{
    /// <summary>No signature was given for its host, nor for its www twin or bare domain.</summary>
    None,

    /// <summary>It carried the signature made for its host.</summary>
    Signed,

    /// <summary>Its host has a signature, which had expired, so none was sent.</summary>
    Expired,

    /// <summary>Its host has none, but its www twin or bare domain has one: each domain needs its own.</summary>
    OtherHost
}
