using System.Text;

namespace LolDataExtractor.Core;

public enum PreviewKind
{
    None,
    Image,
    Model,
    Audio,
    Text,
    Hex,
    Message,
}

/// <summary>Everything the preview panel needs for one entry; only the fields of <see cref="Kind"/> are set.</summary>
public sealed class PreviewResult
{
    public PreviewKind Kind { get; init; } = PreviewKind.None;

    /// <summary>One line of metadata shown under the file name.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Explanation for <see cref="PreviewKind.Message"/> and for decode failures.</summary>
    public string? Message { get; init; }

    public TextureImage? Image { get; init; }

    /// <summary>Bytes of an image the UI can decode itself (png / jpeg passthrough).</summary>
    public byte[]? RawImage { get; init; }

    public ModelPreview? Model { get; init; }

    public WwiseBank? Bank { get; init; }

    /// <summary>Bytes of a single audio stream (<c>.wem</c> / <c>.wav</c>).</summary>
    public byte[]? RawAudio { get; init; }

    public string? Text { get; init; }

    public string? HexDump { get; init; }

    public bool Truncated { get; init; }

    public static PreviewResult Unsupported(string message) => new() { Kind = PreviewKind.Message, Message = message };

    public static PreviewResult Failed(string summary, string message) =>
        new() { Kind = PreviewKind.Message, Summary = summary, Message = message };
}

/// <summary>
/// Decides how an entry can be previewed and materialises that preview: images are
/// decoded to RGBA, models to meshes, sound banks to a stream list, text to a string
/// and everything else to a hex dump. Decode failures come back as a message instead
/// of an exception so the UI can always show why something is not previewable.
/// </summary>
public static class PreviewService
{
    public const int MaxMediaBytes = 128 << 20;
    public const int TextHeadBytes = 256 << 10;
    public const int HexHeadBytes = 8 << 10;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "tex", "dds", "tga", "png", "jpg", "jpeg",
    };

    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "scb", "sco", "skn", "skl", "anm", "wgeo", "mapgeo", "nvr", "preload",
    };

    private static readonly HashSet<string> RenderableModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "scb", "sco", "skn",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "bnk", "wpk", "wem", "wav", "ogg", "mp3", "opus",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "json", "xml", "ini", "cfg", "lua", "luac", "yaml", "yml", "stringtable",
        "manifest", "pyf", "sproject", "subchunktoc", "legacydirlistinfo", "rec", "csv", "md",
    };

    /// <summary>Cheap classification from the extension only; the bytes may refine it later.</summary>
    public static PreviewKind KindOf(string? extension)
    {
        var ext = extension?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
        if (ImageExtensions.Contains(ext)) return PreviewKind.Image;
        if (ModelExtensions.Contains(ext)) return PreviewKind.Model;
        if (AudioExtensions.Contains(ext)) return PreviewKind.Audio;
        if (TextExtensions.Contains(ext)) return PreviewKind.Text;
        return PreviewKind.Hex;
    }

    public static PreviewResult Load(Stream stream, long size, WadEntryInfo entry)
    {
        var extension = entry.Extension;
        var kind = KindOf(extension);

        if (kind is PreviewKind.Image or PreviewKind.Model)
        {
            if (size > MaxMediaBytes)
                return PreviewResult.Failed(Summary(entry), $"文件 {ExtractionSummary.Format(size)} 超出预览上限 {ExtractionSummary.Format(MaxMediaBytes)}");

            var bytes = ReadUpTo(stream, (int)size);
            return kind == PreviewKind.Image
                ? LoadImage(bytes, extension)
                : LoadModel(bytes, extension);
        }

        if (kind == PreviewKind.Audio)
        {
            if (size > MaxMediaBytes)
                return PreviewResult.Failed(Summary(entry), $"音频包 {ExtractionSummary.Format(size)} 超出预览上限");

            var bytes = ReadUpTo(stream, (int)size);
            return LoadAudio(bytes, extension);
        }

        var head = ReadUpTo(stream, kind == PreviewKind.Text ? TextHeadBytes : HexHeadBytes);

        // Pathless entries have no extension: content sniffing decides between text and hex.
        if (extension.Length == 0 && FileTypeSniffer.LooksLikeText(head)) kind = PreviewKind.Text;

        // Extensions lie: some ".rec"/".subchunktoc" files are binary tables, so the bytes win.
        if (kind == PreviewKind.Text && head.Length > 0 && !FileTypeSniffer.LooksLikeText(head))
            kind = PreviewKind.Hex;

        return kind == PreviewKind.Text ? LoadText(head, size) : LoadHex(head, size);
    }

    public static PreviewResult LoadImage(byte[] bytes, string extension)
    {
        try
        {
            if (extension is "png" or "jpg" or "jpeg")
                return new PreviewResult { Kind = PreviewKind.Image, Summary = "直读图片", RawImage = bytes };

            var image = TextureDecoder.Decode(bytes, extension);
            return new PreviewResult
            {
                Kind = PreviewKind.Image,
                Summary = image.Describe,
                Image = image,
            };
        }
        catch (Exception ex)
        {
            return PreviewResult.Failed("贴图", $"无法解码这张贴图：{ex.Message}");
        }
    }

    public static PreviewResult LoadModel(byte[] bytes, string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (!RenderableModelExtensions.Contains(ext))
        {
            var hint = ext switch
            {
                "skl" => "骨骼定义文件，需配合 .skn 使用，暂不做 3D 预览",
                "anm" => "骨骼动画文件，暂不做 3D 预览",
                "mapgeo" => "地图几何文件（体量很大），暂不做 3D 预览",
                "wgeo" => "世界几何文件，暂不做 3D 预览",
                "nvr" => "NVR 网格，暂不做 3D 预览",
                "preload" => "资源预载清单，不是几何数据",
                _ => "暂不支持预览的模型格式",
            };
            return new PreviewResult
            {
                Kind = PreviewKind.Message,
                Summary = "模型元信息",
                Message = hint,
                HexDump = HexDump(bytes, 2048),
            };
        }

        try
        {
            var model = ModelDecoder.Decode(bytes, extension);
            return new PreviewResult
            {
                Kind = PreviewKind.Model,
                Summary = $"{model.Kind} · {model.Describe}",
                Model = model,
            };
        }
        catch (Exception ex)
        {
            return PreviewResult.Failed("模型", $"无法解析这个模型：{ex.Message}");
        }
    }

    public static PreviewResult LoadAudio(byte[] bytes, string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (ext is "bnk" or "wpk")
        {
            try
            {
                var bank = WwiseBank.Parse(bytes);
                if (!bank.HasSounds)
                    return PreviewResult.Failed("音频包", bank.Format == "wpk"
                        ? "这个语音包里没有可播放的音频流"
                        : "这个 .bnk 只有事件或元数据，没有内嵌音频流；同名的 .wpk 或 _audio.bnk 里才是真正的语音与音效");

                var codecs = bank.Sounds.Select(s => s.Codec).Distinct().ToArray();
                return new PreviewResult
                {
                    Kind = PreviewKind.Audio,
                    Summary = $"{bank.Sounds.Count} 条音频流 · {string.Join("/", codecs)} · {bank.FormatLabel} v{bank.Version}",
                    Bank = bank,
                };
            }
            catch (Exception ex)
            {
                return PreviewResult.Failed("音频包", $"无法解析音频包：{ex.Message}");
            }
        }

        return new PreviewResult
        {
            Kind = PreviewKind.Audio,
            Summary = "单条音频，可直接试听",
            RawAudio = bytes,
        };
    }

    public static PreviewResult LoadText(byte[] bytes, long size)
    {
        var text = DecodeText(bytes);
        return new PreviewResult
        {
            Kind = PreviewKind.Text,
            Summary = size > bytes.Length ? $"显示前 {bytes.Length / 1024} KB / 共 {ExtractionSummary.Format(size)}" : "文本",
            Text = text,
            Truncated = size > bytes.Length,
        };
    }

    public static PreviewResult LoadHex(byte[] bytes, long size)
    {
        return new PreviewResult
        {
            Kind = PreviewKind.Hex,
            Summary = size > bytes.Length ? $"十六进制 · 前 {bytes.Length / 1024} KB / 共 {ExtractionSummary.Format(size)}" : "十六进制",
            HexDump = HexDump(bytes, bytes.Length),
            Message = "二进制数据，未识别出可预览的格式",
        };
    }

    /// <summary>Classic hex + ASCII dump.</summary>
    public static string HexDump(ReadOnlySpan<byte> data, int maxBytes = HexHeadBytes, int bytesPerLine = 16)
    {
        var length = Math.Min(data.Length, maxBytes);
        var builder = new StringBuilder(length / bytesPerLine * 80 + 64);
        var ascii = new StringBuilder(bytesPerLine);

        for (var offset = 0; offset < length; offset += bytesPerLine)
        {
            builder.Append(offset.ToString("X8")).Append("  ");
            ascii.Clear();
            for (var i = 0; i < bytesPerLine; i++)
            {
                if (offset + i < length)
                {
                    var value = data[offset + i];
                    builder.Append(value.ToString("X2")).Append(' ');
                    ascii.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
                }
                else
                {
                    builder.Append("   ");
                }

                if (i == 7) builder.Append(' ');
            }

            builder.Append(' ').Append(ascii).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Decodes text honouring UTF-8/UTF-16 byte order marks.</summary>
    public static string DecodeText(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data[3..]);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data[2..]);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data[2..]);
        return Encoding.UTF8.GetString(data);
    }

    private static string Summary(WadEntryInfo entry)
        => entry.IsNamed ? entry.Path! : PathHashing.ToText(entry.Hash);

    private static byte[] ReadUpTo(Stream stream, int count)
    {
        if (count <= 0) return Array.Empty<byte>();
        var buffer = new byte[count];
        var read = 0;
        int chunk;
        while (read < count && (chunk = stream.Read(buffer, read, count - read)) > 0) read += chunk;
        return read == count ? buffer : buffer[..read];
    }
}
