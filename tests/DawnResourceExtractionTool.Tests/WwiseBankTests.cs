using System.Buffers.Binary;
using System.Text;
using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

public class WwiseBankTests
{
    /// <summary>Builds a bank with two wem streams, the way Wwise lays it out.</summary>
    private static byte[] BuildBank(params (uint Id, byte[] Riff)[] sounds)
    {
        var data = new MemoryStream();
        var index = new MemoryStream();
        var offset = 0;
        foreach (var (id, riff) in sounds)
        {
            var entry = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, id);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), offset);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(8), riff.Length);
            index.Write(entry);
            data.Write(riff);
            offset += riff.Length;
        }

        var bank = new MemoryStream();
        WriteChunk(bank, "BKHD", header =>
        {
            var body = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(body, 145);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x2A);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 0x1D);
            header.Write(body);
        });
        WriteChunk(bank, "DIDX", header => index.WriteTo(header));
        WriteChunk(bank, "DATA", header => data.WriteTo(header));
        return bank.ToArray();
    }

    private static void WriteChunk(Stream stream, string id, Action<Stream> body)
    {
        using var payload = new MemoryStream();
        body(payload);
        var bytes = payload.ToArray();

        stream.Write(Encoding.ASCII.GetBytes(id));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length);
        stream.Write(size);
        stream.Write(bytes);
        if ((bytes.Length & 1) == 1) stream.WriteByte(0);
    }

    /// <summary>A RIFF/wem header with a custom Wwise format tag.</summary>
    private static byte[] BuildWem(int formatTag, int samples)
    {
        var stream = new MemoryStream();
        stream.Write("RIFF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, 36 + samples);
        stream.Write(size);
        stream.Write("WAVE"u8);
        stream.Write("fmt "u8);
        BinaryPrimitives.WriteInt32LittleEndian(size, 16);
        stream.Write(size);
        Span<byte> tag = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tag, (ushort)formatTag);
        stream.Write(tag);
        stream.Write(new byte[14]);
        stream.Write("data"u8);
        BinaryPrimitives.WriteInt32LittleEndian(size, samples);
        stream.Write(size);
        stream.Write(new byte[samples]);
        return stream.ToArray();
    }

    [Fact]
    public void Reads_the_header_and_every_index_entry()
    {
        var bank = WwiseBank.Parse(BuildBank((0x3CEE9E4, BuildWem(0xFFFF, 32)), (0x44E2E82, BuildWem(0x0001, 64))));

        Assert.Equal(145, bank.Version);
        Assert.Equal(0x2Au, bank.BankId);
        Assert.Equal(2, bank.Sounds.Count);
        Assert.True(bank.HasSounds);

        Assert.Equal(0, bank.Sounds[0].Index);
        Assert.Equal(0x3CEE9E4u, bank.Sounds[0].Id);
        Assert.Equal(0, bank.Sounds[0].Offset);
        Assert.Equal("Vorbis (Wwise)", bank.Sounds[0].Codec);

        Assert.Equal(1, bank.Sounds[1].Index);
        Assert.Equal(bank.Sounds[0].Length, bank.Sounds[1].Offset);
        Assert.Equal("PCM", bank.Sounds[1].Codec);
    }

    [Fact]
    public void Extracts_one_stream_at_a_time()
    {
        var first = BuildWem(0xFFFF, 32);
        var second = BuildWem(0x0069, 48);
        var bank = WwiseBank.Parse(BuildBank((1, first), (2, second)));

        Assert.Equal(first, bank.ExtractSound(0));
        Assert.Equal(second, bank.ExtractSound(1));
    }

    [Fact]
    public void Rejects_a_file_without_a_bank_header()
    {
        var bytes = Encoding.ASCII.GetBytes("this is not a sound bank at all");
        Assert.Throws<InvalidDataException>(() => WwiseBank.Parse(bytes));
    }

    [Fact]
    public void Reports_a_bank_without_embedded_media()
    {
        var stream = new MemoryStream();
        WriteChunk(stream, "BKHD", body => body.Write(new byte[12]));
        var bank = WwiseBank.Parse(stream.ToArray());

        Assert.False(bank.HasSounds);
        Assert.Empty(bank.Sounds);
    }

    /// <summary>Builds a Riot voice package (r3d2 index + raw wems), the way the game ships it.</summary>
    private static byte[] BuildWpk(params (string Name, byte[] Riff)[] streams)
    {
        const int recordSize = 40;
        var count = streams.Length;
        var headerSize = 12 + count * 4;
        var payloadOffset = headerSize + count * recordSize;

        var header = new byte[headerSize];
        "r3d2"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), count);

        var records = new byte[count * recordSize];
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12 + i * 4, 4), headerSize + i * recordSize);

            var record = records.AsSpan(i * recordSize, recordSize);
            var name = Encoding.Unicode.GetBytes(streams[i].Name);
            BinaryPrimitives.WriteInt32LittleEndian(record, payloadOffset);
            BinaryPrimitives.WriteInt32LittleEndian(record[4..], streams[i].Riff.Length);
            BinaryPrimitives.WriteInt32LittleEndian(record[8..], name.Length / 2);
            name.CopyTo(record[12..]);
            payloadOffset += streams[i].Riff.Length;
        }

        var file = new MemoryStream();
        file.Write(header);
        file.Write(records);
        foreach (var (_, riff) in streams) file.Write(riff);
        return file.ToArray();
    }

    [Fact]
    public void Reads_a_voice_package_and_its_stream_names()
    {
        var first = BuildWem(0xFFFF, 40);
        var second = BuildWem(0xFFFF, 60);
        var bank = WwiseBank.Parse(BuildWpk(("2219180.wem", first), ("9671784.wem", second)));

        Assert.Equal("wpk", bank.Format);
        Assert.Equal(1, bank.Version);
        Assert.Equal(2, bank.Sounds.Count);
        Assert.Equal("2219180.wem", bank.Sounds[0].Name);
        Assert.Equal("2219180.wem", bank.Sounds[0].Label);
        Assert.Equal("Vorbis (Wwise)", bank.Sounds[0].Codec);
        Assert.Equal(second, bank.ExtractSound(1));
    }

    [Fact]
    public void Previews_a_voice_package_as_audio()
    {
        var result = PreviewService.LoadAudio(BuildWpk(("1.wem", BuildWem(0xFFFF, 32))), "wpk");

        Assert.Equal(PreviewKind.Audio, result.Kind);
        Assert.NotNull(result.Bank);
        Assert.Contains("wpk", result.Summary);
        Assert.Single(result.Bank!.Sounds);
    }

    [Fact]
    public void Rejects_a_voice_package_whose_index_is_broken()
    {
        var bytes = new byte[16];
        "r3d2"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 10_000);

        Assert.Throws<InvalidDataException>(() => WwiseBank.Parse(bytes));
    }
}
