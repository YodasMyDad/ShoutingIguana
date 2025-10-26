using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ShoutingIguana.Plugins.DuplicateContent;

/// <summary>
/// SimHash calculator for near-duplicate detection.
/// </summary>
public static class SimHashCalculator
{
    /// <summary>
    /// Computes 64-bit SimHash of the given text content.
    /// </summary>
    public static ulong ComputeSimHash(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        // Extract words (tokens)
        var tokens = Tokenize(content);
        if (tokens.Count == 0)
        {
            return 0;
        }

        // Initialize bit vector (64 bits)
        var vector = new int[64];

        // Process each token
        foreach (var token in tokens)
        {
            // Hash the token
            var hash = HashToken(token);

            // Add or subtract from vector based on bit value
            for (int i = 0; i < 64; i++)
            {
                ulong bit = (hash >> i) & 1;
                if (bit == 1)
                {
                    vector[i]++;
                }
                else
                {
                    vector[i]--;
                }
            }
        }

        // Convert vector to SimHash
        ulong simHash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (vector[i] > 0)
            {
                simHash |= (1UL << i);
            }
        }

        return simHash;
    }

    /// <summary>
    /// Calculates Hamming distance between two SimHashes.
    /// Distance of 0 = identical, distance of 64 = completely different.
    /// </summary>
    public static int HammingDistance(ulong hash1, ulong hash2)
    {
        ulong xor = hash1 ^ hash2;
        int distance = 0;

        // Count set bits
        while (xor != 0)
        {
            distance++;
            xor &= xor - 1; // Clear least significant bit
        }

        return distance;
    }

    /// <summary>
    /// Calculates similarity percentage between two SimHashes.
    /// 100% = identical, 0% = completely different.
    /// </summary>
    public static double SimilarityPercentage(ulong hash1, ulong hash2)
    {
        int distance = HammingDistance(hash1, hash2);
        return (64.0 - distance) / 64.0 * 100.0;
    }

    private static List<string> Tokenize(string content)
    {
        // Normalize: lowercase, remove extra whitespace, extract words
        var normalized = content.ToLowerInvariant();
        
        // Split on word boundaries and filter
        var words = Regex.Split(normalized, @"\W+")
            .Where(w => w.Length >= 3) // Min 3 characters
            .Where(w => !IsStopWord(w))
            .ToList();

        return words;
    }

    private static ulong HashToken(string token)
    {
        // Use MD5 to hash token to 64-bit value
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = md5.ComputeHash(bytes);
        
        // Take first 8 bytes and convert to ulong
        return BitConverter.ToUInt64(hashBytes, 0);
    }

    private static bool IsStopWord(string word)
    {
        // Common English stop words to ignore
        var stopWords = new HashSet<string>
        {
            "the", "and", "are", "for", "not", "but", "had", "has", "was", "all", "were",
            "when", "your", "can", "said", "there", "use", "each", "which", "she",
            "how", "their", "will", "other", "about", "out", "many", "then", "them",
            "these", "some", "her", "would", "make", "like", "him", "into", "time",
            "look", "two", "more", "write", "see", "number", "way", "could",
            "people", "than", "first", "water", "been", "call", "who", "its", "now",
            "find", "long", "down", "day", "did", "get", "come", "made", "may", "part"
        };

        return stopWords.Contains(word);
    }
}

