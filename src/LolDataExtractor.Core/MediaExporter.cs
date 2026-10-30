using System.IO.Compression;
using NAudio.Lame;
using NAudio.Wave;

namespace LolDataExtractor.Core;

/// <summary>Audio encoding used when audio entries are exported.</summary>
public enum AudioExportFormat
{
    /// <summary>Write the container as it is inside the wad.</summary>
    Keep,

    /// <summary>Decode to plain 16 bit PCM waves.</summary>
    Wav,

    /// <summary>Decode and encode as mp3 (the default; falls back to wav when no encoder is available).</summary>
    Mp3,
}

/// <summary>
/// Turns the raw game formats into files a normal program can open: <c>.tex</c>/<c>.dds</c>
/// become png, the Wwise containers (<c>.wem</c>, <c>.bnk</c>, <c>.wpk</c>) become mp3 or wav.
/// Everything else is copied unchanged, and a failed conversion also falls back to the raw copy,
/// so an export never loses data.
/// </summary>
public static class MediaExporter
{
    /// <summary>True for the texture containers this tool knows how to turn into png.</summary>
    public static bool IsConvertibleTexture(string extension) =>
        extension is "tex" or "dds" or "tga";

    /// <summary>True for the Wwise containers this tool knows how to turn into mp3/wav.</summary>
    public static bool IsConvertibleAudio(string extension) =>
        extension is "wem" or "bnk" or "wpk";

    /// <summary>
    /// The path a converted entry ends up at: a png/audio file, or a folder for a bank that holds
    /// several streams. Used for the "skip existing" check before any work is done.
    /// </summary>
    public static string PredictTarget(string destination, string extension, ExtractionOptions options)
    {
        if (options.ConvertTextures && IsConvertibleTexture(extension))
            return Path.ChangeExtension(destination, ".png");

        if (options.AudioFormat != AudioExportFormat.Keep && IsConvertibleAudio(extension))
        {
            var suffix = options.AudioFormat == AudioExportFormat.Wav ? ".wav" : ".mp3";
            return extension is "bnk" or "wpk"
                ? Path.ChangeExtension(destination, null)!   // a folder for the streams
                : Path.ChangeExtension(destination, suffix);
        }

        return destination;
    }

    /// <summary>
    /// Exports one entry and returns the files (or the stream folder) it produced.
    /// <paramref name="destination"/> is the raw path the entry would have had.
    /// <paramref name="audioStream"/> picks a single stream of a bank or voice package (0-based in
    /// container order); null exports every stream of the container.
    /// </summary>
    public static IReadOnlyList<string> Export(
        WadArchive archive,
        WadEntryInfo entry,
        string destination,
        ExtractionOptions options,
        Action<string>? log = null,
        int? audioStream = null)
    {
        var extension = entry.Extension;

        if (options.ConvertTextures && IsConvertibleTexture(extension))
        {
            try
            {
                var image = TextureDecoder.Decode(ReadAll(archive, entry), extension);
                var png = Path.ChangeExtension(destination, ".png");
                PngEncoder.Write(image, png);
                return new[] { png };
            }
            catch (Exception ex)
            {
                log?.Invoke($"贴图转 PNG 失败，改为原样导出：{entry.Path ?? PathHashing.ToText(entry.Hash)}（{ex.Message}）");
                if (options.MediaOnly) return Array.Empty<string>();
            }
        }

        if (options.AudioFormat != AudioExportFormat.Keep && IsConvertibleAudio(extension))
        {
            try
            {
                var files = ExportAudio(archive, entry, destination, options.AudioFormat, log, audioStream);
                if (files.Count > 0) return files;
            }
            catch (Exception ex)
            {
                log?.Invoke($"音频转码失败，改为原样导出：{entry.Path ?? PathHashing.ToText(entry.Hash)}（{ex.Message}）");
                if (options.MediaOnly) return Array.Empty<string>();
            }
        }

        // A media-only export drops what it cannot turn into a playable or viewable file.
        if (options.MediaOnly) return Array.Empty<string>();

        archive.CopyEntryTo(entry.Hash, destination, overwrite: true);
        return new[] { destination };
    }

    private static List<string> ExportAudio(
        WadArchive archive,
        WadEntryInfo entry,
        string destination,
        AudioExportFormat format,
        Action<string>? log,
        int? streamIndex)
    {
        if (format == AudioExportFormat.Mp3 && !Mp3Encoder.IsAvailable)
            throw new InvalidOperationException($"未找到 mp3 编码器（{Mp3Encoder.Describe}）");

        var extension = entry.Extension;
        var suffix = format == AudioExportFormat.Wav ? ".wav" : ".mp3";
        var temp = Path.Combine(Path.GetTempPath(), "loldata-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var bytes = ReadAll(archive, entry);
            var wavs = extension switch
            {
                "bnk" => DecodeBank(temp, bytes, streamIndex),
                "wpk" => DecodeVoicePackage(temp, bytes, streamIndex),
                _ => DecodeSingleWem(temp, bytes),
            };

            if (wavs.Count == 0) throw new InvalidDataException("音频包里没有可解码的流");

            // A container that holds several streams keeps the one-file-per-stream folder layout even
            // when a single stream was picked, so both kinds of export write the same names.
            var several = wavs.Count > 1 || (streamIndex is not null && StreamCount(bytes) > 1);

            if (!several && !RequiresFolder(extension))
            {
                var target = Path.ChangeExtension(destination, suffix);
                Encode(wavs[0].WavPath, target, format);
                return new List<string> { target };
            }

            // Several streams: one folder per container, numbered in container order.
            var folder = Path.ChangeExtension(destination, null)!;
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (var stream in wavs)
            {
                var name = $"{stream.Index:D3} {SafeName(stream.Label)}{suffix}";
                var target = Path.Combine(folder, name);
                Encode(stream.WavPath, target, format);
                written.Add(target);
            }
            return written;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is harmless.
            }
        }
    }

    /// <summary>A voice package or bank with a single stream still gets its own folder.</summary>
    private static bool RequiresFolder(string extension) => extension is "wpk";

    private readonly record struct DecodedStream(int Index, string Label, string WavPath);

    /// <summary>Decodes a Wwise bank: one vgmstream run writes every subsong as a numbered wav.</summary>
    /// <param name="streamIndex">0-based subsong to decode, or null for all of them.</param>
    private static List<DecodedStream> DecodeBank(string temp, byte[] bytes, int? streamIndex)
    {
        var bankPath = Path.Combine(temp, "bank.bnk");
        File.WriteAllBytes(bankPath, bytes);

        var labels = BankLabels(bytes);
        if (streamIndex is { } picked)
        {
            var known = labels.Count;
            if (known > 0 && picked >= known)
                throw new ArgumentOutOfRangeException(nameof(streamIndex), $"音频包只有 {known} 条流");
            var number = picked + 1;
            var single = Path.Combine(temp, $"{number}.wav");
            VgmstreamTool.ConvertToWav(bankPath, number, single);
            var label = labels.TryGetValue(number, out var name) ? name : $"流 {number}";
            return new List<DecodedStream> { new(number, label, single) };
        }

        var wavFolder = Path.Combine(temp, "wav");
        Directory.CreateDirectory(wavFolder);
        VgmstreamTool.ConvertSubsongs(bankPath, Path.Combine(wavFolder, "?s.wav"));

        var streams = new List<DecodedStream>();
        foreach (var wav in Directory.EnumerateFiles(wavFolder, "*.wav"))
        {
            var index = int.TryParse(Path.GetFileNameWithoutExtension(wav), out var parsed) ? parsed : streams.Count + 1;
            var label = labels.TryGetValue(index, out var name) ? name : $"流 {index}";
            streams.Add(new DecodedStream(index, label, wav));
        }

        return streams.OrderBy(stream => stream.Index).ToList();
    }

    /// <summary>Streams a bank or voice package holds, 0 when its index cannot be read.</summary>
    private static int StreamCount(byte[] bytes)
    {
        try
        {
            return WwiseBank.Parse(bytes).Sounds.Count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Stream labels from the bank's media index, keyed by 1-based subsong number.</summary>
    private static Dictionary<int, string> BankLabels(byte[] bytes)
    {
        var labels = new Dictionary<int, string>();
        try
        {
            var bank = WwiseBank.Parse(bytes);
            for (var i = 0; i < bank.Sounds.Count; i++) labels[i + 1] = bank.Sounds[i].Label;
        }
        catch (Exception)
        {
            // Unreadable index only costs the descriptive names.
        }
        return labels;
    }

    /// <summary>Riot voice package: every stream is a raw wem, decoded one by one.</summary>
    /// <param name="streamIndex">0-based stream to decode, or null for all of them.</param>
    private static List<DecodedStream> DecodeVoicePackage(string temp, byte[] bytes, int? streamIndex)
    {
        var bank = WwiseBank.Parse(bytes);

        if (streamIndex is { } picked)
        {
            if (picked < 0 || picked >= bank.Sounds.Count)
                throw new ArgumentOutOfRangeException(nameof(streamIndex), $"音频包只有 {bank.Sounds.Count} 条流");
            var sound = bank.Sounds[picked];
            var singleWem = Path.Combine(temp, $"{picked + 1:D5}.wem");
            bank.WriteSound(picked, singleWem);
            var singleWav = Path.Combine(temp, $"{picked + 1:D5}.wav");
            VgmstreamTool.ConvertToWav(singleWem, null, singleWav);
            return new List<DecodedStream> { new(picked + 1, sound.Label, singleWav) };
        }

        var streams = new List<DecodedStream>(bank.Sounds.Count);
        for (var i = 0; i < bank.Sounds.Count; i++)
        {
            var sound = bank.Sounds[i];
            var wem = Path.Combine(temp, $"{i + 1:D5}.wem");
            bank.WriteSound(i, wem);
            var wav = Path.Combine(temp, $"{i + 1:D5}.wav");
            VgmstreamTool.ConvertToWav(wem, null, wav);
            streams.Add(new DecodedStream(i + 1, sound.Label, wav));
        }
        return streams;
    }

    private static List<DecodedStream> DecodeSingleWem(string temp, byte[] bytes)
    {
        var wem = Path.Combine(temp, "stream.wem");
        File.WriteAllBytes(wem, bytes);
        var wav = Path.Combine(temp, "stream.wav");
        VgmstreamTool.ConvertToWav(wem, null, wav);
        return new List<DecodedStream> { new(1, Path.GetFileNameWithoutExtension(wem), wav) };
    }

    private static void Encode(string wavPath, string target, AudioExportFormat format)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (format == AudioExportFormat.Wav)
        {
            File.Copy(wavPath, target, overwrite: true);
            return;
        }

        Mp3Encoder.Encode(wavPath, target);
    }

    private static byte[] ReadAll(WadArchive archive, WadEntryInfo entry)
    {
        using var stream = archive.OpenDecompressed(entry.Hash);
        using var buffer = entry.UncompressedSize is > 0 and < int.MaxValue
            ? new MemoryStream((int)entry.UncompressedSize)
            : new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Keeps stream names usable as file names; a voice package names them "12345.wem".</summary>
    private static string SafeName(string label)
    {
        if (label.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)) label = label[..^4];

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(label.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) return "stream";
        return cleaned.Length <= 60 ? cleaned : cleaned[..60];
    }
}

/// <summary>Wraps libmp3lame (through NAudio.Lame) so decoded game audio can be written as mp3.</summary>
public static class Mp3Encoder
{
    public static string Describe => "libmp3lame";

    /// <summary>True when the native lame library loaded, i.e. mp3 output is possible.</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                _ = LameDLL.LoadNativeDLL();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Encodes a PCM wave file to a variable quality mp3.</summary>
    public static void Encode(string wavPath, string mp3Path, LAMEPreset preset = LAMEPreset.VBR_80)
    {
        using var reader = new WaveFileReader(wavPath);
        using var writer = new LameMP3FileWriter(mp3Path, reader.WaveFormat, preset);

        var buffer = new byte[64 * 1024];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) writer.Write(buffer, 0, read);
    }
}
