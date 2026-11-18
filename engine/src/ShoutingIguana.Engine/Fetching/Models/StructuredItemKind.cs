namespace ShoutingIguana.Engine.Fetching.Models;

/// <summary>The kinds of value an RFC 8941 structured field can hold, as <see cref="StructuredFields"/> tells them apart.</summary>
internal enum StructuredItemKind
{
    InnerList,
    Integer,
    Decimal,
    String,
    Token,
    ByteSequence,
    Boolean
}
