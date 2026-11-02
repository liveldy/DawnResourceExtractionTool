using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

public class NameTableTests
{
    [Fact]
    public void Parses_space_and_tab_separated_lines()
    {
        Assert.True(NameTable.TryParseLine("0a1b2c3d4e5f6071 assets/foo/bar.dds", out var hash, out var path));
        Assert.Equal(0x0a1b2c3d4e5f6071UL, hash);
        Assert.Equal("assets/foo/bar.dds", path);

        Assert.True(NameTable.TryParseLine("0a1b2c3d4e5f6071\tassets/foo/baz.dds", out hash, out path));
        Assert.Equal("assets/foo/baz.dds", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("zzzzzzzzzzzzzzzz assets/foo.dds")]
    [InlineData("0a1b2c3d4e5f6071")]
    public void Rejects_malformed_lines(string line) => Assert.False(NameTable.TryParseLine(line, out _, out _));

    [Fact]
    public void Looks_up_paths_by_hash()
    {
        var hash = PathHashing.Compute("assets/characters/ahri/skins/base/ahri_base.skn");
        var table = NameTable.FromMap(new Dictionary<ulong, string> { [hash] = "assets/characters/ahri/skins/base/ahri_base.skn" });

        Assert.Equal(1, table.Count);
        Assert.True(table.TryGetPath(hash, out var path));
        Assert.Equal("assets/characters/ahri/skins/base/ahri_base.skn", path);
        Assert.False(table.TryGetPath(hash + 1, out _));
        Assert.Null(table.GetPath(hash + 1));
    }

    [Fact]
    public void Filters_to_a_subset_of_hashes()
    {
        var keep = PathHashing.Compute("assets/keep.dds");
        var drop = PathHashing.Compute("assets/drop.dds");
        var table = NameTable.FromMap(new Dictionary<ulong, string>
        {
            [keep] = "assets/keep.dds",
            [drop] = "assets/drop.dds",
        });

        var filtered = table.Filter(new HashSet<ulong> { keep });

        Assert.Equal(1, filtered.Count);
        Assert.Equal("assets/keep.dds", filtered.GetPath(keep));
    }

    [Fact]
    public void Binary_index_round_trips()
    {
        var map = Enumerable.Range(0, 500).ToDictionary(
            i => (ulong)(i * 7919),
            i => $"assets/测试/{i}.dds");
        var table = NameTable.FromMap(map);

        var directory = Path.Combine(Path.GetTempPath(), "dawn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "names.idx");

        try
        {
            table.SaveBinary(file);
            var reloaded = NameTable.LoadBinary(file);
            Assert.Equal(table.Count, reloaded.Count);
            foreach (var (hash, path) in map)
                Assert.Equal(path, reloaded.GetPath(hash));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Loads_text_hashtable_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dawn-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "hashes.game.txt.0");
        File.WriteAllText(file, """
            0a1b2c3d4e5f6071 assets/foo/bar.dds
            1111222233334444 assets/foo/baz.bin
            """);

        try
        {
            var table = NameTable.LoadTextFiles(new[] { file });
            Assert.Equal(2, table.Count);
            Assert.Equal("assets/foo/bar.dds", table.GetPath(0x0a1b2c3d4e5f6071UL));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Empty_table_never_resolves()
    {
        Assert.True(NameTable.Empty.IsEmpty);
        Assert.False(NameTable.Empty.TryGetPath(1, out _));
    }
}
