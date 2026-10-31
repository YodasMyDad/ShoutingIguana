using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Word count plus two duplicate-detection keys computed in one pass, without allocating per word: an
/// exact hash of the lowercased words, and a 64-bit SimHash over 3-word shingles for near-duplicates.
/// </summary>
public static class ContentFingerprint
{
    private const int MAX_TOKEN_CHARS = 64;
    private const ulong GOLDEN_RATIO = 0x9E3779B97F4A7C15;
    private const ulong FINALIZER = 0xFF51AFD7ED558CCD;

    public static ContentFingerprintResult Compute(ReadOnlySpan<char> text)
    {
        var exactHash = new XxHash3();
        Span<int> weights = stackalloc int[64];
        Span<char> token = stackalloc char[MAX_TOKEN_CHARS];
        ulong previous = 0, beforePrevious = 0;
        var words = 0;
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && !char.IsLetterOrDigit(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var length = 0;
            while (index < text.Length && char.IsLetterOrDigit(text[index]))
            {
                if (length < MAX_TOKEN_CHARS)
                {
                    token[length++] = char.ToLowerInvariant(text[index]);
                }

                index++;
            }

            var tokenBytes = MemoryMarshal.AsBytes(token[..length]);
            if (words > 0)
            {
                exactHash.Append(" "u8);
            }

            exactHash.Append(tokenBytes);
            var tokenHash = XxHash3.HashToUInt64(tokenBytes);
            words++;

            if (words >= 3)
            {
                AddFeature(weights, Mix(beforePrevious, previous, tokenHash));
            }

            beforePrevious = previous;
            previous = tokenHash;
        }

        switch (words)
        {
            case 0:
                return new ContentFingerprintResult(0, 0, 0);
            case 1:
                AddFeature(weights, previous);
                break;
            case 2:
                AddFeature(weights, Mix(0, beforePrevious, previous));
                break;
        }

        ulong simHash = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (weights[bit] > 0)
            {
                simHash |= 1UL << bit;
            }
        }

        return new ContentFingerprintResult(words, exactHash.GetCurrentHashAsUInt64(), simHash);
    }

    /// <summary>Number of differing bits; 3 or fewer usually means near-duplicate content.</summary>
    public static int Distance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    private static ulong Mix(ulong first, ulong second, ulong third)
    {
        var hash = first * GOLDEN_RATIO;
        hash ^= BitOperations.RotateLeft(second, 21);
        hash ^= BitOperations.RotateLeft(third, 42);
        hash ^= hash >> 33;
        hash *= FINALIZER;
        hash ^= hash >> 33;
        return hash;
    }

    private static void AddFeature(Span<int> weights, ulong feature)
    {
        for (var bit = 0; bit < 64; bit++)
        {
            weights[bit] += ((feature >> bit) & 1) == 1 ? 1 : -1;
        }
    }
}
