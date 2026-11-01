using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ShoutingIguana.Sidecar.Api;
using ShoutingIguana.Sidecar.Errors;

namespace ShoutingIguana.Sidecar.Serialization;

/// <summary>Reads JSON request bodies. Kestrel has already capped them at 1 MB.</summary>
internal static class RequestBody
{
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
            if (optionFieldErrors && OptionField(type, exception.Path) is { } field)
            {
                throw Problems.InvalidOptions(new Dictionary<string, IReadOnlyList<string>> { [field] = ["This value isn't valid here."] });
            }

            throw Problems.BadRequest("The request body isn't the JSON this call takes.", exception.Message);
        }
    }

    /// <summary>
    /// The option a bad value is in: <c>$.options.concurrency</c> or <c>$.extraHeaders['X-Key']</c> names one, if every
    /// property along the path is one the body's type has. A property it doesn't have is a bad request, even when its
    /// name is an option's (<c>startUrl</c> when continuing, <c>concurrency</c> beside <c>options</c>).
    /// </summary>
    private static string? OptionField(JsonTypeInfo type, string? path)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return null;
        }

        string? field = null;
        JsonTypeInfo? current = type;
        foreach (var segment in path[2..].Split('.'))
        {
            // A dictionary's key or a list's index ends the names: what follows is inside that option's value.
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            if (current?.Properties.FirstOrDefault(property => property.Name == name) is not { } property)
            {
                return null;
            }

            field = OptionFields.IsField(name) ? name : field;
            if (bracket >= 0)
            {
                break;
            }

            current = current.Options.TryGetTypeInfo(property.PropertyType, out var next) ? next : null;
        }

        return field;
    }
}
