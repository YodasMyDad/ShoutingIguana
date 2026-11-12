using System.Text.Json;
using ShoutingIguana.Engine.Analysis.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>
/// The analysis's limits, for Settings: what's in force with what each limit is, saving new ones, checking them as
/// they're typed, and putting one or all back to the engine's.
/// </summary>
internal static class AnalysisSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/analysis-settings", (AnalysisSettingsStore settings) => TypedResults.Json(Dto(settings.Current), SidecarJsonContext.Default.AnalysisSettingsDto));

        api.MapPut("/api/analysis-settings", async (HttpRequest request, AnalysisSettingsStore settings) =>
            TypedResults.Json(Dto(settings.Save(await ReadAsync(request).ConfigureAwait(false))), SidecarJsonContext.Default.AnalysisSettingsDto));

        // 200 with the messages, keyed by limit. A body that isn't a JSON object, or a limit that doesn't exist, is a bad
        // request (400), as on every other call.
        api.MapPost("/api/analysis-settings/validate", async (HttpRequest request) =>
            TypedResults.Json(new ValidationDto(AnalysisSettingsStore.Validate(await ReadAsync(request).ConfigureAwait(false))), SidecarJsonContext.Default.ValidationDto));

        api.MapPost("/api/analysis-settings/reset", async (HttpRequest request, AnalysisSettingsStore settings) =>
        {
            var reset = await RequestBody.ReadOptionalAsync(request, SidecarJsonContext.Default.ResetAnalysisSettingsRequest).ConfigureAwait(false);
            return TypedResults.Json(Dto(settings.Reset(reset?.Limits)), SidecarJsonContext.Default.AnalysisSettingsDto);
        });
    }

    /// <summary>The limits sent, by name: a name that's no limit's is a property the API doesn't know.</summary>
    private static async Task<Dictionary<string, JsonElement>> ReadAsync(HttpRequest request)
    {
        var values = await RequestBody.ReadAsync(request, SidecarJsonContext.Default.DictionaryStringJsonElement).ConfigureAwait(false);
        if (values.Keys.FirstOrDefault(name => AnalysisLimits.Find(name) is null) is { } unknown)
        {
            throw Problems.BadRequest("The request body isn't the JSON this call takes.",
                $"There's no limit called '{unknown}': there are {string.Join(", ", AnalysisLimits.All.Select(limit => limit.Id))}.");
        }

        return values;
    }

    private static AnalysisSettingsDto Dto(AnalysisSettings settings) => new(
        AnalysisLimits.Values(settings),
        [.. AnalysisLimits.All.Select(limit => new AnalysisLimitDto(limit.Id, limit.Get(settings), limit.Get(AnalysisLimits.Defaults), limit.Unit, limit.Min, limit.Max, limit.Description))],
        settings == AnalysisLimits.Defaults);
}
