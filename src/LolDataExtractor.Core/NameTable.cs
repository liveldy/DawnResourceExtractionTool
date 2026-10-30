using System.Globalization;
using System.Text;

namespace LolDataExtractor.Core;

/// <summary>
/// Immutable hash to path table. Backed by three flat arrays (sorted hashes, path
/// offsets, UTF-8 blob) so that a 2M entry table loads in well under a second and
/// stays compact in memory.
/// </summary>
public sealed class NameTable
{
    private const uint Magic = 0x584E444C; // "LDNX"
    private const int FormatVersion = 1;

    private readonly ulong[] _hashes;
    private readonly int[] _offsets;
    private readonly byte[] _blob;

    private NameTable(ulong[] hashes, int[] offsets, byte[] blob)
    {
        _hashes = hashes;
        _offsets = offsets;
        _blob = blob;
    }

    public static NameTable Empty { get; } = new(Array.Empty<ulong>(), new[] { 0 }, Array.Empty<byte>());

    public int Count => _hashes.Length;

    public bool IsEmpty => _hashes.Length == 0;

    public bool TryGetPath(ulong hash, out string path)
    {
        var index = Array.BinarySearch(_hashes, hash);
        if (index < 0)
        {
            path = string.Empty;
            return false;
        }

        var start = _offsets[index];
        path = Encoding.UTF8.GetString(_blob, start, _offsets[index + 1] - start);
        return true;
    }

    public string? GetPath(ulong hash) => TryGetPath(hash, out var path) ? path : null;

    /// <summary>Builds a table from "hash path" lines (CommunityDragon hashtable format).</summary>
    public static NameTable LoadTextFiles(IEnumerable<string> files, IProgress<string>? log = null)
    {
        var map = new Dictionary<ulong, string>(2_500_000);
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var added = 0;
            foreach (var line in File.ReadLines(file))
            {
                if (!TryParseLine(line, out var hash, out var path)) continue;
                map[hash] = path;
                added++;
            }
            log?.Report($"{Path.GetFileName(file)}: {added} 条");
        }
        return FromMap(map);
    }

    public static NameTable FromMap(IReadOnlyDictionary<ulong, string> map)
    {
        if (map.Count == 0) return Empty;

        var hashes = map.Keys.ToArray();
        Array.Sort(hashes);

        var offsets = new int[hashes.Length + 1];
        var blob = new MemoryStream(map.Values.Sum(v => v.Length + 1));
        for (var i = 0; i < hashes.Length; i++)
        {
            var path = map[hashes[i]];
            var bytes = Encoding.UTF8.GetBytes(path);
            blob.Write(bytes);
            offsets[i + 1] = offsets[i] + bytes.Length;
        }

        return new NameTable(hashes, offsets, blob.ToArray());
    }

    public static bool TryParseLine(string line, out ulong hash, out string path)
    {
        hash = 0;
        path = string.Empty;
        if (line.Length < 18) return false;

        var separator = line.IndexOfAny(new[] { ' ', '\t' });
        if (separator <= 0 || separator > 17) return false;
        if (!ulong.TryParse(line.AsSpan(0, separator), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash))
            return false;

        path = line[(separator + 1)..].Trim();
        return path.Length > 0;
    }

    /// <summary>Keeps only the entries whose hash is in <paramref name="keep"/>.</summary>
    public NameTable Filter(IReadOnlySet<ulong> keep)
    {
        var map = new Dictionary<ulong, string>(Math.Min(keep.Count, Count));
        foreach (var hash in _hashes)
        {
            if (!keep.Contains(hash)) continue;
            if (TryGetPath(hash, out var path)) map[hash] = path;
        }
        return FromMap(map);
    }

    public void SaveBinary(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = filePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write((uint)_hashes.Length);
            foreach (var hash in _hashes) writer.Write(hash);
            foreach (var offset in _offsets) writer.Write(offset);
            writer.Write(_blob.Length);
            writer.Write(_blob);
        }
        File.Move(temp, filePath, overwrite: true);
    }

    public static NameTable LoadBinary(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException($"{filePath} 不是有效的名称索引文件");
        var version = reader.ReadInt32();
        if (version != FormatVersion) throw new InvalidDataException($"{filePath} 索引版本 {version} 不受支持");

        var count = (int)reader.ReadUInt32();
        var hashes = new ulong[count];
        for (var i = 0; i < count; i++) hashes[i] = reader.ReadUInt64();

        var offsets = new int[count + 1];
        for (var i = 0; i <= count; i++) offsets[i] = reader.ReadInt32();

        var blobLength = reader.ReadInt32();
        var blob = reader.ReadBytes(blobLength);
        if (blob.Length != blobLength) throw new InvalidDataException($"{filePath} 已损坏（路径数据不完整）");

        return new NameTable(hashes, offsets, blob);
    }

    public static NameTable LoadBinaryOrEmpty(string filePath)
        => File.Exists(filePath) ? LoadBinary(filePath) : Empty;
}
