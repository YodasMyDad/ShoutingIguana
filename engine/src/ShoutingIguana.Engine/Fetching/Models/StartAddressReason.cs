namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>Why <see cref="StartAddress.ChooseAsync"/> chose the start URL it did.</summary>
public enum StartAddressReason
{
    /// <summary>The address came with its scheme, and is used as typed: nothing was asked.</summary>
    Typed,

    /// <summary>The site answered on https.</summary>
    Https,

    /// <summary>Nothing answered on https, and the site answered on http.</summary>
    Http,

    /// <summary>Neither answered: https, which the crawl then reports as not answering.</summary>
    NoAnswer
}
