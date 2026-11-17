using System.Globalization;
using ShoutingIguana.Engine.Parsing.Models;
using ShoutingIguana.Engine.Storage.Models;

namespace ShoutingIguana.Engine.Storage;

/// <summary>
/// The images one page shows, for the inspector: each image file once, with the alt text the page gives it, where it
/// sits, and what the file is (its size and pixels after any redirects, as the large-image check judges it). Read on the
/// links' index by source, as a page's links are stored together: a page's own links, at most the parser's 10,000, never
/// the crawl's.
/// </summary>
public static class PageImagesReader
{
    public const int DEFAULT_LIMIT = 100;

    public const int MAX_LIMIT = 500;

    private static readonly LinkPosition[] POSITIONS = Enum.GetValues<LinkPosition>();

    /// <summary>
    /// The page's image files, each once, the largest first (after any redirects; those of no known size last), then in
    /// the order the page shows them; at most <paramref name="limit"/> of them. Null when the crawl has no URL of that id.
    /// A page's image links to itself are left out, as the inspector leaves out its links to itself.
    /// </summary>
    /// <exception cref="UrlQueryException">The limit is out of range.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static PageImages? Read(string databasePath, int urlId, int limit = DEFAULT_LIMIT, ReadCache? cache = null, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MAX_LIMIT)
        {
            UrlGridReader.ThrowIfInvalid(new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["limit"] = [$"limit must be from 1 to {MAX_LIMIT.ToString(CultureInfo.InvariantCulture)}."]
            });
        }

        return GridReadContext.Read(databasePath, cache, cancellationToken, context => Read(context, urlId, limit));
    }

    private static PageImages? Read(GridReadContext context, int urlId, int limit)
    {
        KeyValuePair<string, object>[] parameters = [new("$id", urlId)];
        if (context.Scalar("SELECT COUNT(*) FROM urls WHERE id = $id", parameters) == 0)
        {
            return null;
        }

        // Each image once, in the order the page first shows it.
        List<(int Id, string Url)> images = [];
        Dictionary<int, (long Links, string? Text, int Positions)> seen = [];
        long links = 0;
        using (var command = context.Command($"""
                   SELECT l.to_id, u.url, l.text, l.position FROM links l JOIN urls u ON u.id = l.to_id
                   WHERE l.from_id = $id AND l.to_id <> $id AND l.kind = {(int)LinkKind.Image} ORDER BY l.rowid
                   """, parameters))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                links++;
                var id = reader.GetInt32(0);
                var text = reader.IsDBNull(2) || reader.GetString(2).Length == 0 ? null : reader.GetString(2);
                var position = reader.GetInt64(3);
                var bit = position >= 0 && position < POSITIONS.Length ? 1 << (int)position : 0;
                if (seen.TryGetValue(id, out var image))
                {
                    seen[id] = (image.Links + 1, image.Text ?? text, image.Positions | bit);
                }
                else
                {
                    images.Add((id, reader.GetString(1)));
                    seen[id] = (1, text, bit);
                }
            }
        }

        var facts = UrlFactsReader.Read(context, [.. images.Select(image => image.Id)]);
        var order = images.Select((image, index) => (image, index))
            .OrderBy(entry => facts[entry.image.Id].SizeBytes is null ? 1 : 0)
            .ThenByDescending(entry => facts[entry.image.Id].SizeBytes ?? 0)
            .ThenBy(entry => entry.index)
            .Take(limit)
            .Select(entry => entry.image);
        return new PageImages(urlId, images.Count, links, [
            .. order.Select(image =>
            {
                var (count, text, positions) = seen[image.Id];
                return new PageImage(image.Id, image.Url, count, text, [.. POSITIONS.Where(position => (positions & (1 << (int)position)) != 0)], facts[image.Id]);
            })
        ]);
    }
}
