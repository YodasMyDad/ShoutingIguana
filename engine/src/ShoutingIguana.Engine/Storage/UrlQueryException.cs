namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// A grid query the engine can't run: an unknown column, an operator its type doesn't take, a value of the wrong type, a
/// pattern that doesn't compile, a cursor from another query. <see cref="Errors"/> holds plain sentences, keyed by the
/// field's path in the query (<c>filters[1].value</c>).
/// </summary>
public sealed class UrlQueryException(IReadOnlyDictionary<string, IReadOnlyList<string>> errors)
    : Exception(errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "The query can't be run.")
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; } = errors;

    internal static UrlQueryException For(string field, string message) => new(new Dictionary<string, IReadOnlyList<string>> { [field] = [message] });
}
