using System.Buffers.Binary;
using System.IO.Compression;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// Minimal PNG writer for <see cref="TextureImage"/>: 8 bit RGBA, one IDAT chunk,
/// deflated with <see cref="ZLibStream"/>. The core library targets plain .NET, so the
/// WPF encoders are out of reach here and third party imaging is not needed for this.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes the image as a png file, creating the folder when needed.</summary>
    public static void Write(TextureImage image, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Write(image, file);
    }

    public static void Write(TextureImage image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        if (image.Width <= 0 || image.Height <= 0) throw new InvalidDataException(Strings.T("Core.Png.InvalidSize"));
        if (image.Rgba.Length < image.Width * image.Height * 4) throw new InvalidDataException(Strings.T("Core.Png.ShortPixels"));

        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), image.Height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: truecolour with alpha
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR", header);

        WriteChunk(output, "IDAT", Deflate(image));

        WriteChunk(output, "IEND", Array.Empty<byte>());
        output.Flush();
    }

    /// <summary>Filters every scan line with Sub and deflates the result.</summary>
    private static byte[] Deflate(TextureImage image)
    {
        var width = image.Width;
        var height = image.Height;
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * stride;
            var target = y * (stride + 1);
            raw[target] = 1; // Sub filter: byte minus the byte 4 positions back

            for (var x = 0; x < stride; x++)
            {
                var value = image.Rgba[rowStart + x];
                var left = x >= 4 ? image.Rgba[rowStart + x - 4] : (byte)0;
                raw[target + 1 + x] = (byte)(value - left);
            }
        }

        using var buffer = new MemoryStream(raw.Length / 4 + 1024);
        using (var deflate = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(raw);
        }
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = new byte[4];
        for (var i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(Crc32Update(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(uint crc, byte[] data) => Crc32Update(crc, data);

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
