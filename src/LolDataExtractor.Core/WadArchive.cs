using LeagueToolkit.IO.WadFile;

namespace LolDataExtractor.Core;

/// <summary>Metadata of one entry inside a wad, with the name resolved when possible.</summary>
public sealed record WadEntryInfo(
    ulong Hash,
    string? Path,
    long UncompressedSize,
    int CompressedSize,
    string Storage,
    bool IsRedirection)
{
    public bool IsNamed => !string.IsNullOrEmpty(Path);

    public string FileName => IsNamed ? System.IO.Path.GetFileName(Path!) : PathHashing.ToText(Hash);

    /// <summary>Filename extension, empty when the asset has none.</summary>
    public string Extension
    {
        get
        {
            if (IsNamed)
            {
                var ext = System.IO.Path.GetExtension(Path!);
                if (ext.Length > 1) return ext.TrimStart('.').ToLowerInvariant();
            }
            return string.Empty;
        }
    }

    public string Category => FileTypeSniffer.CategoryOf(Extension);
}

/// <summary>Thin, disposable wrapper over a mounted wad file.</summary>
public sealed class WadArchive : IDisposable
{
    private readonly Wad _wad;
    private bool _disposed;

    private WadArchive(string filePath, Wad wad)
    {
        FilePath = filePath;
        _wad = wad;
    }

    public string FilePath { get; }

    public int EntryCount => _wad.Entries.Count;

    public static WadArchive Open(string filePath) => new(filePath, Wad.Mount(filePath, false));

    public IEnumerable<WadEntryInfo> ReadEntries(NameTable? names)
    {
        foreach (var (hash, entry) in _wad.Entries)
        {
            names ??= NameTable.Empty;
            var path = names.GetPath(hash);
            yield return new WadEntryInfo(
                hash,
                path,
                entry.UncompressedSize,
                entry.CompressedSize,
                entry.Type.ToString(),
                entry.Type == WadEntryType.FileRedirection);
        }
    }

    public Stream OpenDecompressed(ulong hash)
    {
        if (!_wad.Entries.TryGetValue(hash, out var entry))
            throw new KeyNotFoundException($"wad 中不存在 hash {PathHashing.ToText(hash)}");
        return entry.GetDataHandle().GetDecompressedStream();
    }

    /// <summary>Reads the first bytes of an entry; used for content sniffing and preview.</summary>
    public byte[] ReadHead(ulong hash, int maxBytes = 512)
    {
        using var stream = OpenDecompressed(hash);
        var buffer = new byte[maxBytes];
        var read = 0;
        int chunk;
        while (read < maxBytes && (chunk = stream.Read(buffer, read, maxBytes - read)) > 0)
            read += chunk;
        return read == maxBytes ? buffer : buffer[..read];
    }

    public void CopyEntryTo(ulong hash, string destinationPath, bool overwrite)
    {
        var directory = System.IO.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var source = OpenDecompressed(hash);
        using var target = new FileStream(destinationPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        source.CopyTo(target);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wad.Dispose();
    }
}
