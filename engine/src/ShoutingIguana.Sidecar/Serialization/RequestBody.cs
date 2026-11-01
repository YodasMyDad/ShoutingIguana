using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>Reads JSON request bodies. Kestrel has already capped them at 1 MB.</summary>
internal static class RequestBody
{
    private const string OPTIONS_PREFIX = "options.";

    public static async Task<T> ReadAsync<T>(HttpRequest request, JsonTypeInfo<T> type) where T : class =>
        await DeserializeAsync(request, type, optionFieldErrors: false).ConfigureAwait(false)
        ?? throw Problems.BadRequest("This call needs a JSON body.");

    /// <summary>For calls whose fields are all optional: no body at all is null.</summary>
    public static async Task<T?> ReadOptionalAsync<T>(HttpRequest request, JsonTypeInfo<T> type, bool optionFieldErrors = false) where T : class =>
        request.ContentLength == 0 ? null : await DeserializeAsync(request, type, optionFieldErrors).ConfigureAwait(false);

    /// <summary>
    /// For bodies that hold crawl options: a value of the wrong type for an option is reported against that option, as
    /// a validation message would be (<c>invalid-options</c>).
    /// </summary>
    public static async Task<T> ReadOptionsAsync<T>(HttpRequest request, JsonTypeInfo<T> type) where T : class =>
        await DeserializeAsync(request, type, optionFieldErrors: true).ConfigureAwait(false)
        ?? throw Problems.BadRequest("This call needs a JSON body.");

    private static async Task<T?> DeserializeAsync<T>(HttpRequest request, JsonTypeInfo<T> type, bool optionFieldErrors) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, type, request.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            if (optionFieldErrors && OptionField(exception.Path) is { } field)
            {
                throw Problems.InvalidOptions(new Dictionary<string, IReadOnlyList<string>> { [field] = ["This value isn't valid here."] });
            }

            throw Problems.BadRequest("The request body isn't the JSON this call takes.", exception.Message);
        }
    }

    /// <summary><c>$.options.concurrency</c> or <c>$.extraHeaders['X-Key']</c> names the option it is in, if it is in one.</summary>
    private static string? OptionField(string? path)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path[2..];
        if (rest.StartsWith(OPTIONS_PREFIX, StringComparison.Ordinal))
        {
            rest = rest[OPTIONS_PREFIX.Length..];
        }

        var end = rest.IndexOfAny(['.', '[']);
        var field = end < 0 ? rest : rest[..end];
        return OptionFields.IsField(field) ? field : null;
    }
}
