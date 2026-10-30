using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using LeagueToolkit.IO.TEXFile;
using Pfim;

namespace LolDataExtractor.Core;

/// <summary>Decoded texture kept as tightly packed RGBA8 pixels.</summary>
public sealed record TextureImage(int Width, int Height, byte[] Rgba, string Format)
{
    public int Stride => Width * 4;

    public string Describe => $"{Width}×{Height} · {Format}";
}

/// <summary>
/// Turns the block compressed textures of the game into plain RGBA pixels.
/// <c>.tex</c> is unwrapped to DDS with LeagueToolkit, DDS/BC1-BC7 is decoded with
/// BCnEncoder and TGA plus the rare uncompressed DDS goes through Pfim.
/// </summary>
public static class TextureDecoder
{
    private static readonly byte[] DdsMagic = "DDS "u8.ToArray();
    private static readonly byte[] TexMagic = "TEX\0"u8.ToArray();

    /// <summary>Unwraps the game's TEX container into a DDS file image.</summary>
    public static byte[] TexToDds(byte[] tex)
    {
        using var input = new MemoryStream(tex, writable: false);
        var texture = new TEX(input);
        using var output = new MemoryStream();
        texture.ToDds(output, true);
        return output.ToArray();
    }

    /// <summary>Decodes a texture by content, falling back to the file extension.</summary>
    public static TextureImage Decode(byte[] bytes, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var ext = extension?.TrimStart('.').ToLowerInvariant() ?? string.Empty;

        if (StartsWith(bytes, TexMagic) || ext == "tex") return DecodeDds(TexToDds(bytes));
        if (StartsWith(bytes, DdsMagic)) return DecodeDds(bytes);
        if (ext == "tga") return DecodeTga(bytes);

        // Unknown: sniff the DDS magic, otherwise report clearly instead of guessing.
        throw new NotSupportedException($"不支持预览的贴图格式：{(ext.Length == 0 ? "无扩展名" : ext)}");
    }

    public static TextureImage DecodeDds(byte[] dds)
    {
        using var stream = new MemoryStream(dds, writable: false);
        DdsFile file;
        try
        {
            file = DdsFile.Load(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("DDS 头解析失败：" + ex.Message, ex);
        }

        var width = (int)file.header.dwWidth;
        var height = (int)file.header.dwHeight;
        if (width <= 0 || height <= 0) throw new InvalidDataException("DDS 尺寸无效");

        var decoder = new BcDecoder();
        if (decoder.IsSupportedFormat(file))
        {
            var pixels = decoder.Decode(file);
            if (pixels.Length == width * height)
                return new TextureImage(width, height, ToRgba(pixels), decoder.GetFormat(file).ToString());
        }

        // Uncompressed DDS (BGRA/RGBA) is not a BCn format: Pfim reads it natively.
        stream.Position = 0;
        return DecodeWithPfim(stream, dds.Length);
    }

    public static TextureImage DecodeTga(byte[] tga)
    {
        using var stream = new MemoryStream(tga, writable: false);
        return DecodeWithPfim(stream, tga.Length);
    }

    private static TextureImage DecodeWithPfim(Stream stream, int size)
    {
        IImage image;
        try
        {
            image = Pfimage.FromStream(stream, new PfimConfig(bufferSize: Math.Clamp(size, 32768, 8 << 20)));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("贴图解码失败：" + ex.Message, ex);
        }

        using (image)
        {
            var width = image.Width;
            var height = image.Height;
            var rgba = new byte[width * height * 4];
            var source = image.Data;
            var stride = image.Stride;

            switch (image.Format)
            {
                case ImageFormat.Rgba32:
                    // Pfim hands back the file's native BGRA order for TGA (and 32-bit DDS).
                    for (var y = 0; y < height; y++)
                    {
                        var src = y * stride;
                        var dst = y * width * 4;
                        for (var x = 0; x < width; x++, src += 4, dst += 4)
                        {
                            rgba[dst] = source[src + 2];
                            rgba[dst + 1] = source[src + 1];
                            rgba[dst + 2] = source[src];
                            rgba[dst + 3] = source[src + 3];
                        }
                    }
                    break;

                case ImageFormat.Rgb24:
                    for (var y = 0; y < height; y++)
                    {
                        var src = y * stride;
                        var dst = y * width * 4;
                        for (var x = 0; x < width; x++, src += 3, dst += 4)
                        {
                            // 24-bit TGA is BGR just like the 32-bit variant.
                            rgba[dst] = source[src + 2];
                            rgba[dst + 1] = source[src + 1];
                            rgba[dst + 2] = source[src];
                            rgba[dst + 3] = 255;
                        }
                    }
                    break;

                case ImageFormat.Rgb8:
                    for (var i = 0; i < width * height; i++)
                    {
                        var value = source[i];
                        rgba[i * 4] = value;
                        rgba[i * 4 + 1] = value;
                        rgba[i * 4 + 2] = value;
                        rgba[i * 4 + 3] = 255;
                    }
                    break;

                case ImageFormat.Rgba16:
                    for (var i = 0; i < width * height; i++)
                    {
                        rgba[i * 4] = source[i * 8 + 1];
                        rgba[i * 4 + 1] = source[i * 8 + 3];
                        rgba[i * 4 + 2] = source[i * 8 + 5];
                        rgba[i * 4 + 3] = source[i * 8 + 7];
                    }
                    break;

                default:
                    throw new NotSupportedException($"暂不支持 Pfim 的像素格式 {image.Format}");
            }

            return new TextureImage(width, height, rgba, image.Format.ToString());
        }
    }

    private static byte[] ToRgba(ColorRgba32[] pixels)
    {
        var rgba = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = pixels[i].r;
            rgba[i * 4 + 1] = pixels[i].g;
            rgba[i * 4 + 2] = pixels[i].b;
            rgba[i * 4 + 3] = pixels[i].a;
        }
        return rgba;
    }

    private static bool StartsWith(byte[] bytes, byte[] magic)
        => bytes.Length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic);
}
