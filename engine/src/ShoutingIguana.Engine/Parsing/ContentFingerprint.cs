using System.Globalization;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ShoutingIguana.Engine.Parsing.Models;

namespace ShoutingIguana.Engine.Parsing;

/// <summary>
/// Word count plus two duplicate-detection keys computed in one pass, without allocating per word: an
/// exact hash of the lowercased words, and a 256-bit SimHash over 3-word shingles for near-duplicates.
/// A word runs on through combining marks (Hindi vowel signs, decomposed accents), and through invisible
/// joiners and an apostrophe between letters ("don't" is one word), which are left out of its spelling.
/// </summary>
/// <remarks>
/// The SimHash is four 64-bit lanes over the same shingles. Lane 0 is the engine's original SimHash, unchanged, which
/// stored crawls compare against; lanes 1 to 3 hash each word with seeds 1 to 3 instead of 0, so their bits are
/// independent of lane 0's and of each other's. Each bit of two pages' fingerprints differs with probability θ/π, where
/// cos θ is the cosine similarity of their shingle counts (Charikar 2002), so the bits that differ out of all 256 estimate
/// how alike the pages are with a quarter of the variance of one lane's 64.
/// </remarks>
public static class ContentFingerprint
{
    /// <summary>64-bit SimHashes in a fingerprint: lane 0 is <see cref="ContentFingerprintResult.SimHash"/>, then lanes 1 to 3.</summary>
    public const int LANES = 4;

    private const char SOFT_HYPHEN = '\u00AD';
    private const char ZERO_WIDTH_NON_JOINER = '\u200C';
    private const char ZERO_WIDTH_JOINER = '\u200D';
    private const char RIGHT_SINGLE_QUOTATION_MARK = '’';

    private const int MAX_TOKEN_CHARS = 64;
    private const ulong GOLDEN_RATIO = 0x9E3779B97F4A7C15;
    private const ulong FINALIZER = 0xFF51AFD7ED558CCD;

    private static readonly Vector256<int>[] BYTE_SIGNS = ByteSigns();

    public static ContentFingerprintResult Compute(ReadOnlySpan<char> text)
    {
        var exactHash = new XxHash3();
        Span<int> weights = stackalloc int[LANES * 64];
        Span<char> token = stackalloc char[MAX_TOKEN_CHARS];

        // Each lane's hashes of the last two words, which the next word makes a shingle with.
        Span<ulong> previous = stackalloc ulong[LANES];
        Span<ulong> beforePrevious = stackalloc ulong[LANES];
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
            if (IsUnspaced(text[index]))
            {
                // Chinese, Japanese, Thai and their neighbours don't put spaces between words; each character counts as one.
                token[length++] = text[index++];
            }
            else
            {
                for (; index < text.Length && !IsUnspaced(text[index]); index++)
                {
                    var character = text[index];
                    if (char.IsLetterOrDigit(character) || IsCombiningMark(character))
                    {
                        if (length < MAX_TOKEN_CHARS)
                        {
                            token[length++] = char.ToLowerInvariant(character);
                        }
                    }
                    else if (!IsInvisibleJoiner(character) &&
                             !(character is '\'' or RIGHT_SINGLE_QUOTATION_MARK && index + 1 < text.Length && char.IsLetter(text[index + 1])))
                    {
                        break;
                    }
                }
            }

            var tokenBytes = MemoryMarshal.AsBytes(token[..length]);
            if (words > 0)
            {
                exactHash.Append(" "u8);
            }

            exactHash.Append(tokenBytes);
            words++;

            for (var lane = 0; lane < LANES; lane++)
            {
                // Seed 0 is XxHash3's default, so lane 0 is the SimHash as it always was.
                var tokenHash = XxHash3.HashToUInt64(tokenBytes, lane);
                if (words >= 3)
                {
                    AddFeature(LaneWeights(weights, lane), Mix(beforePrevious[lane], previous[lane], tokenHash));
                }

                beforePrevious[lane] = previous[lane];
                previous[lane] = tokenHash;
            }
        }

        if (words == 0)
        {
            return default;
        }

        for (var lane = 0; lane < LANES && words < 3; lane++)
        {
            AddFeature(LaneWeights(weights, lane), words == 1 ? previous[lane] : Mix(0, beforePrevious[lane], previous[lane]));
        }

        return new ContentFingerprintResult(words, exactHash.GetCurrentHashAsUInt64(), SimHashOf(LaneWeights(weights, 0)),
            new SimHashLanes(SimHashOf(LaneWeights(weights, 1)), SimHashOf(LaneWeights(weights, 2)), SimHashOf(LaneWeights(weights, 3))));
    }

    /// <summary>Number of differing bits of one lane: unrelated text is about 32 apart, near-identical text a few.</summary>
    public static int Distance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    /// <summary>
    /// Number of differing bits of the whole 256-bit fingerprint, lane 0 given as its <see cref="ContentFingerprintResult.SimHash"/>:
    /// unrelated text is about 128 apart, and d bits apart estimates a similarity of cos(πd/256).
    /// </summary>
    public static int Distance(ulong left, SimHashLanes leftLanes, ulong right, SimHashLanes rightLanes) =>
        Distance(left, right) + Distance(leftLanes.Lane1, rightLanes.Lane1) + Distance(leftLanes.Lane2, rightLanes.Lane2) +
        Distance(leftLanes.Lane3, rightLanes.Lane3);

    private static bool IsUnspaced(char character) => character is >= '฀' and <= '໿' // Thai, Lao
        or >= 'က' and <= '႟' // Myanmar
        or >= 'ក' and <= '៿' // Khmer
        or >= '぀' and <= 'ヿ' // Hiragana, Katakana
        or >= '㐀' and <= '䶿' // CJK extension A
        or >= '一' and <= '鿿' // CJK unified ideographs
        or >= '豈' and <= '﫿'; // CJK compatibility ideographs

    private static bool IsCombiningMark(char character) => char.GetUnicodeCategory(character)
        is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsInvisibleJoiner(char character) => character is SOFT_HYPHEN or ZERO_WIDTH_NON_JOINER or ZERO_WIDTH_JOINER;

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

    private static Span<int> LaneWeights(Span<int> weights, int lane) => weights.Slice(lane * 64, 64);

    /// <summary>
    /// Adds one shingle's hash to a lane's 64 weights: +1 for each bit it has set, -1 for each it hasn't. Eight bits at a
    /// time, from <see cref="BYTE_SIGNS"/>: a bit at a time, the four lanes of a 500-word page took a third of a millisecond.
    /// </summary>
    private static void AddFeature(Span<int> weights, ulong feature)
    {
        var octets = MemoryMarshal.Cast<int, Vector256<int>>(weights);
        for (var octet = 0; octet < octets.Length; octet++)
        {
            octets[octet] += BYTE_SIGNS[(int)(feature >> (octet * 8)) & 0xFF];
        }
    }

    /// <summary>Each byte value's eight bits, lowest first, as +1 where set and -1 where clear.</summary>
    private static Vector256<int>[] ByteSigns()
    {
        var signs = new Vector256<int>[256];
        Span<int> bits = stackalloc int[8];
        for (var value = 0; value < signs.Length; value++)
        {
            for (var bit = 0; bit < bits.Length; bit++)
            {
                bits[bit] = ((value >> bit) & 1) == 1 ? 1 : -1;
            }

            signs[value] = Vector256.Create<int>(bits);
        }

        return signs;
    }

    /// <summary>Each bit is set where more of the page's shingles had it set than not.</summary>
    private static ulong SimHashOf(ReadOnlySpan<int> weights)
    {
        ulong simHash = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (weights[bit] > 0)
            {
                simHash |= 1UL << bit;
            }
        }

        return simHash;
    }
}
