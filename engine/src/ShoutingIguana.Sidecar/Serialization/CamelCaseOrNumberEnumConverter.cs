using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// Enums written as camelCase strings and read as those or as their number. For the two crawl options that went out
/// as numbers in 2.0.0 to 2.0.3 (<c>javaScriptRendering</c> and a rule's <c>selectorType</c>): a <c>defaults.json</c>
/// saved by those versions still reads.
/// </summary>
internal sealed class CamelCaseOrNumberEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: true)
    where TEnum : struct, Enum;
