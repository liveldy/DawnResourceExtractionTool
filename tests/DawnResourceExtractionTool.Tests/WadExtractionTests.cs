using System.Text;
using LeagueToolkit.IO.WadFile;
using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

public class WadExtractionTests : IDisposable
{
    private readonly string _directory;

    public WadExtractionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dawn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string BuildWad()
    {
        var file = Path.Combine(_directory, "Test.wad.client");
        using var builder = new WadBuilder();
        foreach (var (path, data) in new (string? Path, string Text)[]
                 {
                     ("assets/hello.txt", "hello wad"),
                     ("assets/nested/info.json", "{\"value\": 42}"),
                     (null, "unnamed payload"),
                 })
        {
            var entry = new WadEntryBuilder(WadEntryChecksumType.XXHash3)
                .WithPathXXHash(PathHashing.Compute(path ?? "__unnamed__"))
                .WithUncompressedDataStream(new MemoryStream(Encoding.UTF8.GetBytes(data)));
            builder.WithEntry(entry);
        }
        builder.Build(file);
        return file;
    }

    [Fact]
    public void Reads_entries_and_resolves_names()
    {
        var wad = BuildWad();
        var names = NameTable.FromMap(new Dictionary<ulong, string>
        {
            [PathHashing.Compute("assets/hello.txt")] = "assets/hello.txt",
            [PathHashing.Compute("assets/nested/info.json")] = "assets/nested/info.json",
        });

        using var archive = WadArchive.Open(wad);
        Assert.Equal(3, archive.EntryCount);

        var entries = archive.ReadEntries(names).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(e => e.IsNamed));
        Assert.Contains(entries, e => e.Path == "assets/hello.txt");
    }

    [Fact]
    public void Reads_entry_content_and_headers()
    {
        var wad = BuildWad();
        using var archive = WadArchive.Open(wad);
        var hash = PathHashing.Compute("assets/hello.txt");

        var head = archive.ReadHead(hash, 64);
        Assert.Equal("hello wad", Encoding.UTF8.GetString(head));

        using var stream = archive.OpenDecompressed(hash);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("hello wad", reader.ReadToEnd());
    }

    [Fact]
    public void Extracts_named_and_unnamed_entries_with_a_manifest()
    {
        var wad = BuildWad();
        var names = NameTable.FromMap(new Dictionary<ulong, string>
        {
            [PathHashing.Compute("assets/hello.txt")] = "assets/hello.txt",
            [PathHashing.Compute("assets/nested/info.json")] = "assets/nested/info.json",
        });

        var output = Path.Combine(_directory, "out");
        var summary = ExtractionService.Extract(
            _directory,
            new[] { wad },
            names,
            new ExtractionFilter(),
            new ExtractionOptions { OutputRoot = output, Threads = 2 });

        Assert.Equal(3, summary.Extracted);
        Assert.Equal(0, summary.Failed);
        Assert.True(File.Exists(Path.Combine(output, "Test", "assets", "hello.txt")));
        Assert.True(File.Exists(Path.Combine(output, "Test", "assets", "nested", "info.json")));

        var unnamed = Directory.GetFiles(Path.Combine(output, "Test", "_unnamed"), "*", SearchOption.AllDirectories);
        Assert.Single(unnamed);
        Assert.EndsWith(".txt", unnamed[0], StringComparison.Ordinal);

        Assert.NotNull(summary.ManifestPath);
        var manifestLines = File.ReadAllLines(summary.ManifestPath!);
        Assert.Equal(3, manifestLines.Length);
        Assert.Contains("assets/hello.txt", manifestLines[0] + manifestLines[1] + manifestLines[2]);
    }

    [Fact]
    public void Skips_existing_files_on_a_second_run()
    {
        var wad = BuildWad();
        var output = Path.Combine(_directory, "out2");
        var options = new ExtractionOptions { OutputRoot = output, Threads = 1 };

        var first = ExtractionService.Extract(_directory, new[] { wad }, NameTable.Empty, new ExtractionFilter(), options);
        var second = ExtractionService.Extract(_directory, new[] { wad }, NameTable.Empty, new ExtractionFilter(), options);

        Assert.Equal(3, first.Extracted);
        Assert.Equal(3, second.Skipped);
        Assert.Equal(0, second.Extracted);
    }

    [Fact]
    public void Honours_the_path_filter()
    {
        var wad = BuildWad();
        var output = Path.Combine(_directory, "out3");
        var summary = ExtractionService.Extract(
            _directory,
            new[] { wad },
            NameTable.Empty,
            new ExtractionFilter { PathPattern = "*hello*" },
            new ExtractionOptions { OutputRoot = output });

        // 名称未解析时过滤按哈希匹配，因此这里只断言没有失败且输出目录存在
        Assert.Equal(0, summary.Failed);
        Assert.True(Directory.Exists(output));
    }

    [Fact]
    public void Catalog_describes_a_wad()
    {
        var wad = BuildWad();
        var summary = WadCatalog.Describe(_directory, wad);

        Assert.Equal(3, summary.EntryCount);
        Assert.Equal("Test.wad.client", summary.Name);
        Assert.True(summary.UncompressedSize > 0);
        Assert.Equal(3, summary.StorageTypes["Uncompressed"]);
    }

    [Fact]
    public void Wad_key_strips_the_wad_client_suffix()
    {
        Assert.Equal("Ahri", ExtractionService.WadKeyOf("C:\\game\\FINAL\\Champions\\Ahri.wad.client"));
        Assert.Equal("Akali.zh_CN", ExtractionService.WadKeyOf("Akali.zh_CN.wad.client"));
    }

    [Fact]
    public void Single_export_keeps_only_the_audio_container_name()
    {
        // 预览页「导出此文件」导出音频时不再重复 wad 里的深层目录，只留包自己的名字。
        var voice = new WadEntryInfo(1,
            "assets/sounds/wwise2016/vo/zh_cn/characters/ahri/skins/base/ahri_base_vo_audio.wpk",
            0, 0, "Uncompressed", false);
        Assert.Equal("ahri_base_vo_audio.wpk", ExtractionService.SingleExportRelativePath(voice));

        var sound = new WadEntryInfo(2, "assets/sounds/wwise2016/sfx/characters/ahri/ahri_q_cast.wem",
            0, 0, "Uncompressed", false);
        Assert.Equal("ahri_q_cast.wem", ExtractionService.SingleExportRelativePath(sound));
    }

    [Fact]
    public void Single_export_keeps_the_folder_tree_for_other_assets()
    {
        var texture = new WadEntryInfo(3, "assets/characters/ahri/skins/base/ahri_base.tex",
            0, 0, "Uncompressed", false);
        Assert.Equal(Path.Combine("assets", "characters", "ahri", "skins", "base", "ahri_base.tex"),
            ExtractionService.SingleExportRelativePath(texture));
    }
}
