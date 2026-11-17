namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>What a URL grid column holds, which decides the filters it takes.</summary>
public enum UrlColumnType
{
    Number,
    Text,

    /// <summary>One of a fixed set of values (<see cref="UrlColumnInfo.Values"/>), named in camelCase.</summary>
    Enum,
    Boolean,

    /// <summary>A record or list rather than one value (a redirect's hops): shown only, written as a line of text in an export.</summary>
    Object
}
