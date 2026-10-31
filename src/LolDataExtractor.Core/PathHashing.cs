using System.IO.Hashing;
using System.Text;

namespace LolDataExtractor.Core;

/// <summary>
/// League of Legends wad entries are addressed by a 64 bit hash of the lowercased
/// asset path (XXH64, seed 0). The same value is used by CommunityDragon hashtables.
/// </summary>
public static class PathHashing
{
    public static ulong Compute(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        return Compute(Encoding.UTF8.GetBytes(assetPath.ToLowerInvariant()));
    }

    public static ulong Compute(ReadOnlySpan<byte> utf8LowerPath) => XxHash64.HashToUInt64(utf8LowerPath);

    /// <summary>Renders a hash in the 16 character form used by hashtable files.</summary>
    public static string ToText(ulong hash) => hash.ToString("x16");
}
