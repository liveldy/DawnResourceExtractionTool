using System.Buffers.Binary;
using System.Text;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// One sound stream inside a Wwise sound bank. The 4-byte id is the Wwise
/// short name (often a numeric hash) and is what the game uses to play it;
/// voice packages name their streams instead (<c>2219180.wem</c>).
/// </summary>
public sealed record WwiseSound(int Index, uint Id, int Offset, int Length, int FormatTag, string? Name = null)
{
    /// <summary>Name when the container carries one, otherwise the id.</summary>
    public string Label => string.IsNullOrEmpty(Name) ? Id.ToString() : Name;

    /// <summary>Wwise fmt tag of the embedded stream, e.g. "Vorbis (Wwise)".</summary>
    public string Codec => FormatTag switch
    {
        0x0001 => "PCM",
        0x0002 => "ADPCM",
        0x0003 => "IEEE float",
        0x0061 => "XMA",
        0x0069 => "Vorbis",
        0xFFFE => "Extensible",
        0xFFFF => "Vorbis (Wwise)",
        _ => FormatTag == 0 ? Strings.T("Core.Wwise.UnknownCodec") : $"0x{FormatTag:X4}",
    };
}

/// <summary>
/// Reader for the two containers League of Legends ships audio in:
/// a Wwise sound bank (<c>.bnk</c>: <c>BKHD</c> header, <c>DIDX</c> media index,
/// <c>DATA</c> payload of RIFF/wem streams) and a Riot voice package (<c>.wpk</c>:
/// <c>r3d2</c> header, one index record per stream, then the raw wem streams).
/// </summary>
public sealed class WwiseBank
{
    private readonly byte[] _payload;

    private WwiseBank(int version, uint bankId, uint languageId, byte[] payload, IReadOnlyList<WwiseSound> sounds,
        string format = "bnk")
    {
        Version = version;
        BankId = bankId;
        LanguageId = languageId;
        _payload = payload;
        Sounds = sounds;
        Format = format;
    }

    public int Version { get; }

    /// <summary><c>bnk</c> or <c>wpk</c>.</summary>
    public string Format { get; }

    public string FormatLabel => Format == "wpk" ? Strings.T("Core.Wwise.VoicePackage") : Strings.T("Core.Wwise.SoundBank");

    public uint BankId { get; }

    public uint LanguageId { get; }

    public IReadOnlyList<WwiseSound> Sounds { get; }

    public bool HasSounds => Sounds.Count > 0;

    /// <summary>Parses a whole bank or voice package held in memory; both are a few MB at most.</summary>
    public static WwiseBank Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("r3d2"u8)) return ParseWpk(bytes);
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("BKHD"u8))
            throw new InvalidDataException(Strings.T("Core.Wwise.NotABank"));

        var version = 0;
        uint bankId = 0;
        uint languageId = 0;
        var index = new List<(uint Id, int Offset, int Length)>();
        var payload = Array.Empty<byte>();

        var position = 0;
        while (position + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, position, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4, 4));
            var body = position + 8;
            if (size < 0 || body + size > bytes.Length) break;

            switch (id)
            {
                case "BKHD":
                    if (size >= 4) version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body, 4));
                    if (size >= 12)
                    {
                        bankId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 4, 4));
                        languageId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 8, 4));
                    }
                    break;

                case "DIDX":
                    for (var offset = 0; offset + 12 <= size; offset += 12)
                    {
                        index.Add((
                            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + offset, 4)),
                            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + offset + 4, 4)),
                            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(body + offset + 8, 4))));
                    }
                    break;

                case "DATA":
                    payload = bytes.AsSpan(body, size).ToArray();
                    break;
            }

            position = body + size;
            if ((size & 1) == 1) position++; // chunks are padded to an even length
        }

        var sounds = new List<WwiseSound>(index.Count);
        for (var i = 0; i < index.Count; i++)
        {
            var (soundId, offset, length) = index[i];
            if (offset < 0 || length <= 0 || offset + length > payload.Length) continue;
            sounds.Add(new WwiseSound(i, soundId, offset, length, ReadFormatTag(payload, offset, length)));
        }

        return new WwiseBank(version, bankId, languageId, payload, sounds);
    }

    /// <summary>
    /// Reads a Riot voice package: <c>r3d2</c>, version, stream count, an offset per
    /// stream, then one 12-byte record per stream (data offset, size, name length) with
    /// the UTF-16 name inline; the streams themselves follow the index as plain wems.
    /// </summary>
    private static WwiseBank ParseWpk(byte[] bytes)
    {
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        if (count < 0 || 12L + count * 4L > bytes.Length)
            throw new InvalidDataException(Strings.T("Core.Wwise.WpkIndexCorrupt"));

        var sounds = new List<WwiseSound>(count);
        for (var i = 0; i < count; i++)
        {
            var record = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12 + i * 4, 4));
            if (record < 0 || record + 12 > bytes.Length) continue;

            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(record, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(record + 4, 4));
            var nameChars = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(record + 8, 4));

            string? name = null;
            if (nameChars > 0 && nameChars <= 256 && record + 12L + nameChars * 2L <= bytes.Length)
                name = Encoding.Unicode.GetString(bytes, record + 12, nameChars * 2).TrimEnd('\0');

            if (offset < 0 || length <= 0 || offset + (long)length > bytes.Length) continue;
            sounds.Add(new WwiseSound(i, 0, offset, length, ReadFormatTag(bytes, offset, length), name));
        }

        // Streams live in the same buffer, so the whole file is the payload.
        return new WwiseBank((int)version, 0, 0, bytes, sounds, "wpk");
    }

    /// <summary>Raw bytes of one embedded wem stream.</summary>
    public byte[] ExtractSound(int index)
    {
        if (index < 0 || index >= Sounds.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var sound = Sounds[index];
        return _payload.AsSpan(sound.Offset, sound.Length).ToArray();
    }

    /// <summary>Writes one embedded wem stream to disk (used before transcoding).</summary>
    public void WriteSound(int index, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, ExtractSound(index));
    }

    /// <summary>Reads the format tag of the RIFF/wem header that starts at <paramref name="offset"/>.</summary>
    private static int ReadFormatTag(byte[] payload, int offset, int length)
    {
        var end = Math.Min(offset + length, payload.Length);
        if (end - offset < 24) return 0;

        var header = payload.AsSpan(offset, end - offset);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header.Slice(8, 4).SequenceEqual("WAVE"u8)) return 0;

        // Walk the RIFF chunks until "fmt " is found; the tag is its first 16-bit field.
        var cursor = 12;
        while (cursor + 8 <= header.Length)
        {
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(cursor + 4, 4));
            if (header.Slice(cursor, 4).SequenceEqual("fmt "u8) && chunkSize >= 2 && cursor + 10 <= header.Length)
                return BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(cursor + 8, 2));
            if (chunkSize < 0) break;
            cursor += 8 + chunkSize + (chunkSize & 1);
        }

        return 0;
    }
}
