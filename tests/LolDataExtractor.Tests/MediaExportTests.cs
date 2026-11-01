using System.IO.Compression;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class MediaExportTests
{
    [Fact]
    public void Png_writer_produces_a_readable_truecolour_image()
    {
        // 2x2 with a red, a green, a blue and a transparent pixel.
        var rgba = new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 0, 0, 0, 0,
        };

        using var buffer = new MemoryStream();
        PngEncoder.Write(new TextureImage(2, 2, rgba, "test"), buffer);
        var png = buffer.ToArray();

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        // Width and height sit in the IHDR chunk right after the signature.
        Assert.Equal(2, ReadInt32(png, 16));
        Assert.Equal(2, ReadInt32(png, 20));
        Assert.Equal(8, png[24]);   // bit depth
        Assert.Equal(6, png[25]);   // RGBA

        var pixels = InflateFirstIdat(png);
        Assert.Equal(2 * (2 * 4 + 1), pixels.Length);
        Assert.Equal(1, pixels[0]);   // Sub filter on the first row
        Assert.Equal(255, pixels[1]); // red, nothing to subtract from
        Assert.Equal(255, pixels[4]); // green channel is 0 - 0
        Assert.Equal(1, pixels[9]);   // the second row starts with its own filter byte
        Assert.Equal(0, pixels[10]);  // its first pixel is blue, i.e. 0 in the red channel
    }

    [Fact]
    public void Textures_are_exported_as_png_and_audio_as_mp3()
    {
        var options = new ExtractionOptions { OutputRoot = Path.GetTempPath() };

        Assert.Equal(@"C:\out\ahri.png", MediaExporter.PredictTarget(@"C:\out\ahri.tex", "tex", options));
        Assert.Equal(@"C:\out\ahri.png", MediaExporter.PredictTarget(@"C:\out\ahri.dds", "dds", options));
        Assert.Equal(@"C:\out\ahri.mp3", MediaExporter.PredictTarget(@"C:\out\ahri.wem", "wem", options));
        Assert.Equal(@"C:\out\ahri.bnk", MediaExporter.PredictTarget(@"C:\out\ahri.bnk", "bnk", options));

        // Everything else keeps its own path.
        Assert.Equal(@"C:\out\ahri.skn", MediaExporter.PredictTarget(@"C:\out\ahri.skn", "skn", options));
    }

    [Fact]
    public void Raw_export_keeps_the_container()
    {
        var options = new ExtractionOptions
        {
            OutputRoot = Path.GetTempPath(),
            ConvertTextures = false,
            AudioFormat = AudioExportFormat.Keep,
        };

        Assert.Equal(@"C:\out\ahri.tex", MediaExporter.PredictTarget(@"C:\out\ahri.tex", "tex", options));
        Assert.Equal(@"C:\out\ahri.wem", MediaExporter.PredictTarget(@"C:\out\ahri.wem", "wem", options));
    }

    [Fact]
    public void Wav_export_uses_the_wav_extension()
    {
        var options = new ExtractionOptions { OutputRoot = Path.GetTempPath(), AudioFormat = AudioExportFormat.Wav };

        Assert.Equal(@"C:\out\ahri.wav", MediaExporter.PredictTarget(@"C:\out\ahri.wem", "wem", options));
    }

    private static int ReadInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    /// <summary>Collects the IDAT payload and inflates it, the way a png reader would.</summary>
    private static byte[] InflateFirstIdat(byte[] png)
    {
        using var output = new MemoryStream();
        var position = 8;
        while (position + 8 <= png.Length)
        {
            var length = ReadInt32(png, position);
            var type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            if (type == "IDAT")
            {
                using var inflate = new ZLibStream(new MemoryStream(png, position + 8, length), CompressionMode.Decompress);
                inflate.CopyTo(output);
            }
            position += 12 + length;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }
}

public class AudioNoteStoreTests
{
    [Fact]
    public void Notes_survive_a_reload_and_removal_is_saved_too()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loldata-notes-{Guid.NewGuid():N}.json");
        try
        {
            var store = AudioNoteStore.Load(path);
            var key = AudioNoteStore.KeyOf(@"D:\Game\DATA\FINAL\Champions\Ahri.zh_CN.wad.client", 0x733F89FF0544F395, 4);

            store.Set(key, "我们去某点真正的垃圾吧。");
            store.Set(key, "   ");   // a blank note is a removal
            store.Set(key, "脆弱的根基当然经不起风雨。");

            var reloaded = AudioNoteStore.Load(path);
            Assert.Equal(1, reloaded.Count);
            Assert.Equal("脆弱的根基当然经不起风雨。", reloaded.Get(key));
            Assert.Contains("脆弱的根基", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Keys_name_the_wad_the_hash_and_the_stream()
    {
        var key = AudioNoteStore.KeyOf(@"C:\x\Ahri.zh_CN.wad.client", 0x0102030405060708, 11);

        Assert.Equal("Ahri.zh_CN.wad.client|0102030405060708|11", key);
    }

    [Fact]
    public void A_corrupt_file_starts_from_an_empty_set()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loldata-notes-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Equal(0, AudioNoteStore.Load(path).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class SpeechLanguageTests
{
    [Theory]
    [InlineData("Ahri.zh_CN.wad.client", "zh")]
    [InlineData("Ahri.en_US.wad.client", "en")]
    [InlineData("Ahri.ja_JP.wad.client", "ja")]
    [InlineData("Ahri.ko_KR.wad.client", "ko")]
    [InlineData("Ahri.wad.client", "auto")]
    [InlineData("Map11.wad.client", "auto")]
    public void The_locale_in_the_wad_name_picks_the_language(string wadName, string expected)
        => Assert.Equal(expected, SpeechRecognizer.LanguageOf(wadName));
}
