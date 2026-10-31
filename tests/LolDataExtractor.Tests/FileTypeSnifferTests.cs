using System.Text;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class FileTypeSnifferTests
{
    private static ReadOnlySpan<byte> Head(string text) => Encoding.ASCII.GetBytes(text);

    [Theory]
    [InlineData("DDS |\0\0\0\0", "dds")]
    [InlineData("TEX\0", "tex")]
    [InlineData("r3d2Mesh", "scb")]
    [InlineData("r3d2anmd", "anm")]
    [InlineData("r3d2canm", "anm")]
    [InlineData("PROP\x03\x00\x00\x00", "bin")]
    [InlineData("BKHD4\0\0\0", "bnk")]
    [InlineData("OggS\x00\x02", "ogg")]
    [InlineData("RIFF\0\0\0\0WAVEfmt ", "wem")]
    [InlineData("{\"name\": 1}", "txt")]
    public void Detects_extension_from_content(string header, string expected)
        => Assert.Equal(expected, FileTypeSniffer.DetectExtension(Head(header)));

    [Fact]
    public void Detects_skn_and_skl_signatures()
    {
        Assert.Equal("skn", FileTypeSniffer.DetectExtension(new byte[] { 0x33, 0x22, 0x11, 0x00, 0x04, 0x00 }));
        Assert.Equal("skl", FileTypeSniffer.DetectExtension(new byte[] { 0xE0, 0x17, 0x00, 0x00, 0xC3, 0x4F, 0xFD, 0x22 }));
    }

    /// <summary>WebM is a Matroska/EBML container; MP4 announces itself with an ftyp box.</summary>
    [Fact]
    public void Detects_video_signatures()
    {
        Assert.Equal("webm", FileTypeSniffer.DetectExtension(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00 }));
        Assert.Equal("mp4", FileTypeSniffer.DetectExtension(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }));
    }

    [Fact]
    public void Returns_null_for_unknown_binary()
        => Assert.Null(FileTypeSniffer.DetectExtension(new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 }));

    [Theory]
    [InlineData("tex", "texture")]
    [InlineData(".dds", "texture")]
    [InlineData("skn", "model")]
    [InlineData("bnk", "audio")]
    [InlineData("webm", "video")]
    [InlineData(".webm", "video")]
    [InlineData("mp4", "video")]
    [InlineData("bin", "bin")]
    [InlineData("json", "text")]
    [InlineData("dx11", "shader")]
    [InlineData("unknown-ext", "other")]
    [InlineData("", "other")]
    public void Maps_extensions_to_categories(string extension, string expected)
        => Assert.Equal(expected, FileTypeSniffer.CategoryOf(extension));

    [Fact]
    public void Decodes_text_without_bom()
    {
        var data = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("英雄联盟")).ToArray();
        Assert.Equal("英雄联盟", FileTypeSniffer.DecodeText(data));
    }
}
