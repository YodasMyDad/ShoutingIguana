using ShoutingIguana.Engine.Fetching;
using ShoutingIguana.Engine.Fetching.Models;
using ShoutingIguana.Sidecar.Api.Models;
using ShoutingIguana.Sidecar.Errors;
using ShoutingIguana.Sidecar.Options;
using ShoutingIguana.Sidecar.Serialization;

namespace ShoutingIguana.Sidecar.Api;

/// <summary>The user's defaults for new crawls, and validation for the crawl settings form.</summary>
internal static class OptionsEndpoints
{
    private const string INVALID_OPTIONS = "invalid-options";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/api/crawl-options/defaults", (DefaultsStore defaults) =>
            TypedResults.Json(new DefaultsDto(defaults.Read(), SpeedPresets.ALL), SidecarJsonContext.Default.DefaultsDto));

        api.MapPut("/api/crawl-options/defaults", async (HttpRequest request, DefaultsStore defaults) =>
        {
            var options = await RequestBody.ReadOptionsAsync(request, SidecarJsonContext.Default.CrawlOptionsDto).ConfigureAwait(false);
            return TypedResults.Json(new DefaultsDto(defaults.Save(options), SpeedPresets.ALL), SidecarJsonContext.Default.DefaultsDto);
        });

        // 200 with the messages, keyed by field: a value of the wrong type is one of them. A body that isn't JSON, or a
        // property the options don't have, is a bad request (400), as on every other call.
        api.MapPost("/api/crawl-options/validate", async (HttpRequest request) =>
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> errors;
            try
            {
                var options = await RequestBody.ReadOptionsAsync(request, SidecarJsonContext.Default.CrawlOptionsDto).ConfigureAwait(false);
                var crawlOptions = options.ToCrawlOptions(out var mappingErrors);
                errors = OptionFields.Merge(mappingErrors, crawlOptions.ValidateFields());
            }
            catch (ProblemException refused) when (refused.Problem is { Type: INVALID_OPTIONS, Errors: { } fieldErrors })
            {
                errors = fieldErrors;
            }

            return TypedResults.Json(new ValidationDto(errors), SidecarJsonContext.Default.ValidationDto);
        });

        // An address typed without a scheme: https when the site answers there, else http when it answers there. Asked
        // with the options the crawl would start with, so a proxy the site needs is used. Options a crawl would refuse
        // are refused here too (their start URL aside: the address is what's asked about), and nothing is asked.
        api.MapPost("/api/crawl-options/start-url", async (HttpRequest request, DefaultsStore defaults) =>
        {
            var body = await RequestBody.ReadAsync(request, SidecarJsonContext.Default.StartUrlRequest).ConfigureAwait(false);
            var address = body.Address ?? throw Problems.BadRequest("Give the address to start from.");
            var options = (body.Options ?? defaults.Read()).ToCrawlOptions(out var mappingErrors);
            var errors = OptionFields.Merge(mappingErrors, options.ValidateFields())
                .Where(field => field.Key != OptionFields.START_URL)
                .ToDictionary(StringComparer.Ordinal);
            if (errors.Count > 0)
            {
                throw Problems.InvalidOptions(errors);
            }

            var choice = await StartAddress.ChooseAsync(address, options, request.HttpContext.RequestAborted).ConfigureAwait(false);
            return TypedResults.Json(new StartUrlDto(choice.StartUrl, ReasonName(choice.Reason)), SidecarJsonContext.Default.StartUrlDto);
        });
    }

    private static string ReasonName(StartAddressReason reason) => reason switch
    {
        StartAddressReason.Https => "https",
        StartAddressReason.Http => "http",
        StartAddressReason.NoAnswer => "noAnswer",
        _ => "typed"
    };
}
