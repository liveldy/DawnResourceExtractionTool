using System.Text;

namespace LolDataExtractor.Core;

/// <summary>
/// Content based identification for WAD entries. Most assets inside a wad carry a
/// signature, which lets the extractor pick a usable extension even when the path
/// name could not be resolved through a hashtable.
/// </summary>
public static class FileTypeSniffer
{
    private readonly record struct Signature(int Offset, byte[] Magic, string Extension, string Category);

    private static readonly Signature[] Signatures =
    {
        new(0, "DDS "u8.ToArray(), "dds", "texture"),
        new(0, "TEX\0"u8.ToArray(), "tex", "texture"),
        new(0, "r3d2Mesh"u8.ToArray(), "scb", "model"),
        new(0, "r3d2sklt"u8.ToArray(), "skl", "model"),
        new(4, new byte[] { 0xC3, 0x4F, 0xFD, 0x22 }, "skl", "model"),
        new(0, new byte[] { 0x33, 0x22, 0x11, 0x00 }, "skn", "model"),
        new(0, "r3d2anmd"u8.ToArray(), "anm", "model"),
        new(0, "r3d2canm"u8.ToArray(), "anm", "model"),
        new(0, "PROP"u8.ToArray(), "bin", "bin"),
        new(0, "BKHD"u8.ToArray(), "bnk", "audio"),
        new(0, "OggS"u8.ToArray(), "ogg", "audio"),
        new(0, "OEGM"u8.ToArray(), "mapgeo", "model"),
        new(0, "NVR\0"u8.ToArray(), "nvr", "model"),
        new(0, "\x1bLua"u8.ToArray(), "luac", "text"),
    };

    private static readonly Dictionary<string, string> CategoryByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tex"] = "texture", ["dds"] = "texture", ["tga"] = "texture", ["png"] = "texture", ["jpg"] = "texture",
        ["skn"] = "model", ["skl"] = "model", ["scb"] = "model", ["anm"] = "model", ["wgeo"] = "model",
        ["mapgeo"] = "model", ["nvr"] = "model", ["obj"] = "model", ["preload"] = "model",
        ["bnk"] = "audio", ["wem"] = "audio", ["wav"] = "audio", ["ogg"] = "audio", ["mp3"] = "audio",
        ["wpk"] = "audio", ["opus"] = "audio",
        ["bin"] = "bin", ["inibin"] = "bin", ["troybin"] = "bin", ["dat"] = "bin",
        ["json"] = "text", ["xml"] = "text", ["ini"] = "text", ["yaml"] = "text", ["yml"] = "text",
        ["txt"] = "text", ["lua"] = "text", ["luac"] = "text", ["stringtable"] = "text", ["pyf"] = "text",
        ["cfg"] = "text", ["manifest"] = "text", ["sproject"] = "text", ["subchunktoc"] = "text",
        ["legacydirlistinfo"] = "text", ["rec"] = "text",
        ["dx11"] = "shader", ["dx9"] = "shader", ["ps_2_0"] = "shader", ["vs_2_0"] = "shader",
    };

    public static string CategoryOf(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return "other";
        var ext = extension.TrimStart('.');
        return CategoryByExtension.TryGetValue(ext, out var category) ? category : "other";
    }

    /// <summary>Returns the extension implied by the content signature, or null.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> head)
    {
        foreach (var signature in Signatures)
        {
            if (head.Length < signature.Offset + signature.Magic.Length) continue;
            if (head.Slice(signature.Offset, signature.Magic.Length).SequenceEqual(signature.Magic))
                return signature.Extension;
        }

        // RIFF container: Wwise audio (.wem) and plain waves share the same envelope.
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WAVE"u8))
            return "wem";

        return IsProbablyText(head) ? "txt" : null;
    }

    private static bool IsProbablyText(ReadOnlySpan<byte> head)
    {
        if (head.Length == 0) return false;
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return true;
        var sample = head[..Math.Min(head.Length, 256)];
        foreach (var b in sample)
        {
            if (b == 0) return false;
            if (b < 0x09 || (b > 0x0D && b < 0x20)) return false;
        }
        return true;
    }

    /// <summary>True when the content looks like UTF-8/UTF-16 text (used for preview).</summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> head) => IsProbablyText(head);

    public static string DecodeText(ReadOnlySpan<byte> data, int maxBytes = 4096)
    {
        var slice = data[..Math.Min(data.Length, maxBytes)];
        if (slice.Length >= 3 && slice[0] == 0xEF && slice[1] == 0xBB && slice[2] == 0xBF) slice = slice[3..];
        return Encoding.UTF8.GetString(slice);
    }
}
