using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoutingIguana.Cli.Output;

/// <summary>An enum written as its camelCase name, as the sidecar writes it: <c>notComparedThen</c>, <c>countsOnly</c>.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;
