using System.Text;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class PreviewServiceTests
{
    private static WadEntryInfo Entry(string path, long size = 10)
        => new(PathHashing.Compute(path), path, size, (int)size, "Uncompressed", false);

    [Theory]
    [InlineData("tex", PreviewKind.Image)]
    [InlineData("dds", PreviewKind.Image)]
    [InlineData("tga", PreviewKind.Image)]
    [InlineData("skn", PreviewKind.Model)]
    [InlineData("scb", PreviewKind.Model)]
    [InlineData("skl", PreviewKind.Model)]
    [InlineData("bnk", PreviewKind.Audio)]
    [InlineData("wem", PreviewKind.Audio)]
    [InlineData("xml", PreviewKind.Text)]
    [InlineData("json", PreviewKind.Text)]
    [InlineData("bin", PreviewKind.Hex)]
    [InlineData("", PreviewKind.Hex)]
    public void Classifies_by_extension(string extension, PreviewKind expected)
        => Assert.Equal(expected, PreviewService.KindOf(extension));

    [Fact]
    public void Reads_text_entries_as_text()
    {
        var text = "hello 英雄联盟";
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = PreviewService.Load(new MemoryStream(bytes), bytes.Length, Entry("assets/hello.txt"));

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Binary_content_in_a_text_extension_falls_back_to_hex()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x00, 0xFF, 0x10, 0x00, 0x20 };
        var result = PreviewService.Load(new MemoryStream(bytes), bytes.Length, Entry("assets/thing.rec"));

        Assert.Equal(PreviewKind.Hex, result.Kind);
        Assert.Contains("00000000", result.HexDump);
    }

    [Fact]
    public void Unnamed_binary_entries_get_a_hex_dump()
    {
        var bytes = new byte[32];
        var entry = new WadEntryInfo(PathHashing.Compute("x"), null, bytes.Length, bytes.Length, "Uncompressed", false);
        var result = PreviewService.Load(new MemoryStream(bytes), bytes.Length, entry);

        Assert.Equal(PreviewKind.Hex, result.Kind);
        Assert.NotNull(result.HexDump);
        Assert.Contains("00 00 00 00", result.HexDump);
    }

    [Fact]
    public void Broken_images_surface_a_message_instead_of_throwing()
    {
        var bytes = new byte[] { 0x44, 0x44, 0x53, 0x20, 1, 2, 3 };
        var result = PreviewService.Load(new MemoryStream(bytes), bytes.Length, Entry("assets/broken.dds"));

        Assert.Equal(PreviewKind.Message, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Hex_dump_aligns_offsets_and_ascii()
    {
        var bytes = Encoding.ASCII.GetBytes("0123456789abcdefXYZ");
        var dump = PreviewService.HexDump(bytes, 32);

        var lines = dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("00000000  30 31 32 33", lines[0]);
        Assert.Contains("0123456789abcdef", lines[0]);
        Assert.StartsWith("00000010", lines[1]);
        Assert.Contains("XYZ", lines[1]);
    }

    [Fact]
    public void Decodes_utf16_text_with_a_byte_order_mark()
        => Assert.Equal("abc", PreviewService.DecodeText(new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x62, 0x00, 0x63, 0x00 }));

    [Fact]
    public void Audio_banks_without_streams_report_the_reason()
    {
        // BKHD + empty DIDX + empty DATA: an events bank rather than an audio bank.
        var bytes = new byte[]
        {
            (byte)'B', (byte)'K', (byte)'H', (byte)'D', 4, 0, 0, 0, 1, 0, 0, 0,
            (byte)'D', (byte)'I', (byte)'D', (byte)'X', 0, 0, 0, 0,
            (byte)'D', (byte)'A', (byte)'T', (byte)'A', 0, 0, 0, 0,
        };
        var result = PreviewService.Load(new MemoryStream(bytes), bytes.Length, Entry("assets/events.bnk"));

        Assert.Equal(PreviewKind.Message, result.Kind);
        Assert.Contains("没有内嵌音频流", result.Message);
        Assert.Contains(".wpk", result.Message);
    }
}
