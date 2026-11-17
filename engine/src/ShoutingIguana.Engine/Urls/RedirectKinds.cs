namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// What a redirect changes, as the URL grid's <c>redirectKind</c> column and a target's fact give it: its kinds, comma
/// separated in this order, of <see cref="HTTP_TO_HTTPS"/>, <see cref="HTTPS_TO_HTTP"/>, <see cref="ADDS_WWW"/>,
/// <see cref="REMOVES_WWW"/>, <see cref="OTHER_HOST"/>, <see cref="ADDS_SLASH"/>, <see cref="REMOVES_SLASH"/>,
/// <see cref="CHANGES_CASE"/>; <see cref="OTHER"/> alone when it changes the path or query some other way.
/// </summary>
public static class RedirectKinds
{
    public const string HTTP_TO_HTTPS = "httpToHttps";
    public const string HTTPS_TO_HTTP = "httpsToHttp";
    public const string ADDS_WWW = "addsWww";
    public const string REMOVES_WWW = "removesWww";
    public const string OTHER_HOST = "otherHost";
    public const string ADDS_SLASH = "addsSlash";
    public const string REMOVES_SLASH = "removesSlash";
    public const string CHANGES_CASE = "changesCase";
    public const string OTHER = "other";

    /// <summary>The kinds of the redirect from <paramref name="from"/> to <paramref name="to"/>; null when either isn't an absolute URL.</summary>
    public static string? Of(string? from, string? to)
    {
        if (!Uri.TryCreate(from, UriKind.Absolute, out var source) || !Uri.TryCreate(to, UriKind.Absolute, out var target))
        {
            return null;
        }

        List<string> kinds = [];
        if (source.Scheme == Uri.UriSchemeHttp && target.Scheme == Uri.UriSchemeHttps)
        {
            kinds.Add(HTTP_TO_HTTPS);
        }
        else if (source.Scheme == Uri.UriSchemeHttps && target.Scheme == Uri.UriSchemeHttp)
        {
            kinds.Add(HTTPS_TO_HTTP);
        }

        var sourceHost = WithoutWww(source.Host);
        var sameSite = string.Equals(sourceHost, WithoutWww(target.Host), StringComparison.OrdinalIgnoreCase) && source.Port == target.Port ||
                       string.Equals(sourceHost, WithoutWww(target.Host), StringComparison.OrdinalIgnoreCase) && source.IsDefaultPort && target.IsDefaultPort;
        if (!sameSite)
        {
            // Another host: what changes on the way there says nothing more.
            kinds.Add(OTHER_HOST);
            return string.Join(',', kinds);
        }

        var sourceWww = HasWww(source.Host);
        var targetWww = HasWww(target.Host);
        if (!sourceWww && targetWww)
        {
            kinds.Add(ADDS_WWW);
        }
        else if (sourceWww && !targetWww)
        {
            kinds.Add(REMOVES_WWW);
        }

        var path = source.AbsolutePath;
        var targetPath = target.AbsolutePath;
        var sameQuery = source.Query == target.Query;
        if (sameQuery && path != targetPath)
        {
            var slash = string.Empty;
            if (targetPath == path + "/" || string.Equals(targetPath, path + "/", StringComparison.OrdinalIgnoreCase))
            {
                slash = ADDS_SLASH;
                targetPath = targetPath[..^1];
            }
            else if (path == targetPath + "/" || string.Equals(path, targetPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                slash = REMOVES_SLASH;
                path = path[..^1];
            }

            if (path == targetPath)
            {
                kinds.Add(slash);
            }
            else if (string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                kinds.AddRange(slash.Length > 0 ? [slash, CHANGES_CASE] : [CHANGES_CASE]);
            }
            else
            {
                return OtherWith(kinds);
            }
        }
        else if (!sameQuery)
        {
            return OtherWith(kinds);
        }

        return kinds.Count > 0 ? string.Join(',', kinds) : OTHER;
    }

    /// <summary>The page moves somewhere else: "other", after what else changed on the way (a scheme, www).</summary>
    private static string OtherWith(List<string> kinds) => string.Join(',', [.. kinds, OTHER]);

    private static bool HasWww(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static string WithoutWww(string host) => HasWww(host) ? host[4..] : host;
}
