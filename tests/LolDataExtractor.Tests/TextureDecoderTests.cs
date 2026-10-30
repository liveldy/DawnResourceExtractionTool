using System.Buffers.Binary;
using System.Text;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class TextureDecoderTests
{
    /// <summary>Minimal DDS container around one raw BC1 block (4×4 texels, 8 bytes).</summary>
    private static byte[] BuildBc1Dds(byte[] block)
    {
        var dds = new byte[128 + block.Length];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);

        var span = dds.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, 124);            // dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0x81007);   // caps|height|width|pixelformat|linearsize
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 4);         // height
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 4);        // width
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)block.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 0);        // depth
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], 1);        // mipmaps

        BinaryPrimitives.WriteUInt32LittleEndian(span[72..], 32);       // ddspf.dwSize
        BinaryPrimitives.WriteUInt32LittleEndian(span[76..], 0x4);      // DDPF_FOURCC
        Encoding.ASCII.GetBytes("DXT1").CopyTo(dds, 4 + 80);
        BinaryPrimitives.WriteUInt32LittleEndian(span[100..], 0x1000);  // DDSCAPS_TEXTURE

        block.CopyTo(dds, 128);
        return dds;
    }

    /// <summary>One BC1 block: colour0 pure white, colour1 black, every texel selects colour0.</summary>
    private static byte[] WhiteBlock()
    {
        var block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, 0xFFFF); // colour0 = RGB 565 white
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), 0x0000);
        return block;
    }

    private static byte[] BuildTga(byte[] bgra, int width, int height)
    {
        var tga = new byte[18 + bgra.Length];
        tga[2] = 2; // uncompressed true colour
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
        tga[16] = 32;
        tga[17] = 0x28; // 8 alpha bits, origin top-left
        bgra.CopyTo(tga, 18);
        return tga;
    }

    [Fact]
    public void Decodes_a_bc1_dds_to_rgba()
    {
        var image = TextureDecoder.DecodeDds(BuildBc1Dds(WhiteBlock()));

        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal(4 * 4 * 4, image.Rgba.Length);
        Assert.All(Enumerable.Range(0, 16), i =>
        {
            Assert.Equal(255, image.Rgba[i * 4]);
            Assert.Equal(255, image.Rgba[i * 4 + 1]);
            Assert.Equal(255, image.Rgba[i * 4 + 2]);
            Assert.Equal(255, image.Rgba[i * 4 + 3]);
        });
    }

    [Fact]
    public void Decodes_an_uncompressed_tga()
    {
        // BGRA: red, green, blue, half transparent white
        var pixels = new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 128,
        };

        var image = TextureDecoder.DecodeTga(BuildTga(pixels, 2, 2));

        Assert.Equal(2, image.Width);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Rgba[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, image.Rgba[4..8]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Rgba[8..12]);
        Assert.Equal(128, image.Rgba[15]);
    }

    [Fact]
    public void Decodes_a_24_bit_tga()
    {
        // TGA stores BGR: this pixel is pure red.
        var pixels = new byte[] { 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255 };
        var tga = new byte[18 + pixels.Length];
        tga[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), 2);
        tga[16] = 24;
        tga[17] = 0x20; // origin top-left
        pixels.CopyTo(tga, 18);

        var image = TextureDecoder.DecodeTga(tga);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Rgba[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, image.Rgba[4..8]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Rgba[8..12]);
    }

    [Fact]
    public void Reports_a_broken_tex_container()
    {
        var broken = new byte[] { 0x54, 0x45, 0x58, 0x00, 0x01, 0x02 };
        Assert.ThrowsAny<Exception>(() => TextureDecoder.Decode(broken, "tex"));
    }

    [Fact]
    public void Unknown_texture_formats_are_reported()
    {
        var error = Assert.Throws<NotSupportedException>(() => TextureDecoder.Decode(new byte[] { 1, 2, 3, 4 }, "psd"));
        Assert.Contains("psd", error.Message);
    }
}
