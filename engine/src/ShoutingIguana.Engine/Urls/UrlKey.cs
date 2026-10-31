using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace ShoutingIguana.Engine.Urls;

/// <summary>
/// 128-bit hash of a normalised URL. Lets the writer keep a seen-set of millions of URLs in a few
/// dozen MB instead of holding every URL string in memory. Collisions are not a practical concern at
/// 128 bits.
/// </summary>
internal static class UrlKey
{
    public static UInt128 Compute(string normalizedUrl) =>
        XxHash128.HashToUInt128(MemoryMarshal.AsBytes(normalizedUrl.AsSpan()));
}
