using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>
/// Enums as camelCase strings (<c>"running"</c>, <c>"googlebotSmartphone"</c>). Numbers are refused, so a request can't
/// carry a value the enum doesn't define.
/// </summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;
