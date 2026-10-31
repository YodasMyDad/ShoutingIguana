using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Storage.Models;

/// <summary>The detail blob stored per internal URL: response headers plus the parsed page facts.</summary>
public sealed record ResponseDetails(IReadOnlyList<ResponseHeader> Headers, PageFacts? Page);
