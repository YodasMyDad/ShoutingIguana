using System.Text.Json;

namespace SpikeSidecar;

/// <summary>Deterministic, realistic-looking crawl rows: row i is always the same, so no storage is needed.</summary>
public static class UrlRowGenerator
{
    public const int Total = 1_000_000;

    private const string Origin = "https://www.example-homeware.co.uk/";

    private static readonly string[] Sections =
        ["products", "category", "blog", "guides", "support", "brands", "collections", "help-centre", "stores", "offers"];

    private static readonly string[] Words =
    [
        "oak", "dining", "table", "chair", "garden", "outdoor", "lighting", "pendant", "kitchen", "storage", "bedroom",
        "wardrobe", "sofa", "corner", "velvet", "linen", "cotton", "rug", "mirror", "bathroom", "shower", "tap", "basin",
        "cabinet", "shelf", "desk", "office", "ergonomic", "lamp", "floor", "wall", "ceramic", "tile", "paint", "matt",
        "gloss", "white", "grey", "blue", "green", "black", "brass", "copper", "steel", "large", "small", "set", "pack",
        "premium", "classic", "modern", "vintage", "nordic", "industrial", "rustic", "coastal",
    ];

    private static readonly string[] QueryStrings =
        ["?colour=grey", "?sort=price-asc&page=3", "?size=large&colour=white", "?utm_source=newsletter&utm_medium=email", "?page=12"];

    public static void Write(Utf8JsonWriter writer, int index)
    {
        var rng = new SplitMix((ulong)index * 0x9E3779B97F4A7C15UL + 17);

        var status = PickStatus(rng.Next() % 1000);
        var isHtml = true;
        var contentType = "text/html; charset=utf-8";
        if (status == 200)
        {
            var kind = rng.Next() % 100;
            (contentType, isHtml) = kind switch
            {
                < 85 => ("text/html; charset=utf-8", true),
                < 90 => ("application/pdf", false),
                < 95 => ("image/jpeg", false),
                < 98 => ("text/css", false),
                _ => ("application/javascript", false),
            };
        }

        var url = BuildUrl(ref rng, index);

        string? reason = status switch
        {
            >= 300 and < 400 => "Redirected",
            >= 400 and < 500 => "Client error",
            >= 500 => "Server error",
            _ => null,
        };
        if (reason is null && isHtml)
        {
            var roll = rng.Next() % 100;
            reason = roll switch
            {
                < 5 => "Noindex",
                < 12 => "Canonicalised",
                < 13 => "Blocked by robots.txt",
                _ => null,
            };
        }

        long size = status switch
        {
            >= 300 and < 400 => (long)(rng.Next() % 900),
            _ when contentType == "application/pdf" => 100_000 + (long)(rng.Next() % 8_000_000),
            _ when contentType == "image/jpeg" => 20_000 + (long)(rng.Next() % 2_000_000),
            _ => 8_000 + (long)(rng.Next() % 340_000),
        };

        var depth = index == 0 ? 0 : 1 + (int)Math.Min(11, -Math.Log(rng.NextDouble() + 1e-9) * 2.2);
        var responseMs = rng.Next() % 50 == 0
            ? 1_000 + (int)(rng.Next() % 4_000)
            : (int)Math.Exp(4 + 2 * rng.NextDouble());
        var inlinks = (int)Math.Min(50_000, 1 / Math.Pow(rng.NextDouble() + 1e-6, 0.8)) - 1;
        var outlinks = isHtml && status == 200 ? 15 + (int)(rng.Next() % 265) : 0;
        var words = isHtml && status == 200 ? 150 + (int)(rng.Next() % 3_350) : 0;
        var title = isHtml && status == 200 && rng.Next() % 100 >= 3 ? BuildTitle(ref rng) : "";

        writer.WriteStartObject();
        writer.WriteNumber("index", index);
        writer.WriteString("url", url);
        writer.WriteNumber("status", status);
        writer.WriteBoolean("indexable", reason is null);
        if (reason is null)
        {
            writer.WriteNull("indexabilityReason");
        }
        else
        {
            writer.WriteString("indexabilityReason", reason);
        }
        writer.WriteString("title", title);
        writer.WriteString("contentType", contentType);
        writer.WriteNumber("size", size);
        writer.WriteNumber("depth", depth);
        writer.WriteNumber("responseMs", responseMs);
        writer.WriteNumber("inlinks", inlinks);
        writer.WriteNumber("outlinks", outlinks);
        writer.WriteNumber("words", words);
        writer.WriteEndObject();
    }

    private static int PickStatus(ulong roll) => roll switch
    {
        < 880 => 200,
        < 930 => 301,
        < 940 => 302,
        < 975 => 404,
        < 980 => 410,
        < 990 => 500,
        < 995 => 503,
        _ => 403,
    };

    private static string BuildUrl(ref SplitMix rng, int index)
    {
        var target = 60 + (int)(rng.Next() % 56);
        var builder = new System.Text.StringBuilder(Origin, 140);
        builder.Append(Sections[rng.Next() % (ulong)Sections.Length]).Append('/');
        builder.Append(Words[rng.Next() % (ulong)Words.Length]).Append('-').Append(Words[rng.Next() % (ulong)Words.Length]).Append('/');
        var suffix = "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var first = true;
        while (first || builder.Length + suffix.Length < target - 4)
        {
            if (!first)
            {
                builder.Append('-');
            }
            builder.Append(Words[rng.Next() % (ulong)Words.Length]);
            first = false;
        }
        builder.Append(suffix);
        var query = QueryStrings[rng.Next() % (ulong)QueryStrings.Length];
        if (rng.Next() % 10 == 0 && builder.Length + query.Length <= 120)
        {
            builder.Append(query);
        }
        return builder.ToString();
    }

    private static string BuildTitle(ref SplitMix rng)
    {
        var count = 3 + (int)(rng.Next() % 6);
        var builder = new System.Text.StringBuilder(90);
        for (var i = 0; i < count; i++)
        {
            var word = Words[rng.Next() % (ulong)Words.Length];
            if (i > 0)
            {
                builder.Append(' ');
            }
            builder.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
        }
        return builder.Append(" | Example Homeware").ToString();
    }

    private struct SplitMix(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
    }
}
