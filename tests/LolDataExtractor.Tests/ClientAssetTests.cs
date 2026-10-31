using System.Text;
using LeagueToolkit.IO.WadFile;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>The client side: plugin folder discovery and the second name table.</summary>
public class ClientAssetTests : IDisposable
{
    private readonly string _root;

    public ClientAssetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Game", "DATA"));
        File.WriteAllBytes(Path.Combine(_root, "Game", "DATA", "DATA.wad.client"), new byte[8]);
        var plugin = Path.Combine(_root, "LeagueClient", "plugins", "rcp-be-lol-game-data");
        Directory.CreateDirectory(plugin);
        BuildWad(Path.Combine(plugin, "assets.wad"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void BuildWad(string file)
    {
        using var builder = new WadBuilder();
        var entry = new WadEntryBuilder(WadEntryChecksumType.XXHash3)
            .WithPathXXHash(PathHashing.Compute("plugins/rcp-be-lol-game-data/global/zh_cn/v1/champions/103.json"))
            .WithUncompressedDataStream(new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"test\"}")));
        builder.WithEntry(entry);
        builder.Build(file);
    }

    private string Plugins => Path.Combine(_root, "LeagueClient", "plugins");

    [Fact]
    public void Finds_the_plugins_folder_from_the_install_root()
        => Assert.Equal(Plugins, LolClient.TryLocatePlugins(_root));

    [Fact]
    public void Finds_the_plugins_folder_from_the_client_folder()
        => Assert.Equal(Plugins, LolClient.TryLocatePlugins(Path.Combine(_root, "LeagueClient")));

    [Fact]
    public void Finds_the_plugins_folder_from_the_plugins_folder_itself()
        => Assert.Equal(Plugins, LolClient.TryLocatePlugins(Plugins));

    [Fact]
    public void The_game_data_folder_never_reaches_over_to_the_client()
    {
        // Scanning follows the picked folder: DATA holds the game archives only.
        Assert.Null(LolClient.TryLocatePlugins(Path.Combine(_root, "Game", "DATA")));
        Assert.Null(LolClient.TryLocatePlugins(Path.Combine(_root, "Game")));
    }

    [Fact]
    public void Nothing_below_the_plugins_folder_is_resolved_by_climbing()
    {
        // A single plugin folder is scanned as the folder it is, not widened to its parent.
        Assert.Null(LolClient.TryLocatePlugins(Path.Combine(Plugins, "rcp-be-lol-game-data")));
        Assert.Null(LolClient.TryLocatePlugins(Path.Combine(Plugins, "rcp-be-lol-game-data", "assets.wad")));
    }

    [Fact]
    public void Returns_null_for_a_folder_without_client_wads()
    {
        var unrelated = Path.Combine(Path.GetTempPath(), "loldata-unrelated", Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(unrelated, "LeagueClient", "plugins");
        Directory.CreateDirectory(plugins);
        try
        {
            // The plugins folder exists but holds no wad at all.
            Assert.Null(LolClient.TryLocatePlugins(unrelated));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }

        Assert.Null(LolClient.TryLocatePlugins(string.Empty));
        Assert.Null(LolClient.TryLocatePlugins(null));
    }

    [Fact]
    public void Enumerates_client_wads_and_prefixes_their_relative_path()
    {
        var wads = LolClient.EnumerateWadFiles(Plugins);
        Assert.Single(wads);
        Assert.Equal("LeagueClient/rcp-be-lol-game-data/assets.wad", LolClient.RelativePath(Plugins, wads[0]));
    }

    [Fact]
    public void Client_scan_tags_the_source_and_keeps_the_game_tree_apart()
    {
        var summaries = WadCatalog.ScanClient(Plugins);
        var summary = Assert.Single(summaries);
        Assert.Equal(WadSources.Client, summary.Source);
        Assert.True(summary.IsClient);
        Assert.StartsWith("LeagueClient/", summary.RelativePath);
        Assert.Equal(1, summary.EntryCount);
    }

    [Fact]
    public void Union_merges_two_name_tables()
    {
        var left = Table(("a/b.txt", "a/b.txt"));
        var right = Table(("c/d.json", "c/d.json"));
        var merged = left.Union(right);

        Assert.Equal(2, merged.Count);
        Assert.Equal("a/b.txt", merged.GetPath(PathHashing.Compute("a/b.txt")));
        Assert.Equal("c/d.json", merged.GetPath(PathHashing.Compute("c/d.json")));
        Assert.Null(merged.GetPath(PathHashing.Compute("missing.txt")));
    }

    [Fact]
    public void Union_prefers_the_left_path_for_the_same_hash()
    {
        var left = Table(("same.txt", "from-game"));
        var right = Table(("same.txt", "from-client"));
        var merged = left.Union(right);

        Assert.Equal(1, merged.Count);
        Assert.Equal("from-game", merged.GetPath(PathHashing.Compute("same.txt")));
    }

    [Fact]
    public void Union_handles_empty_sides()
    {
        var table = Table(("x.txt", "x.txt"));
        Assert.Same(table, table.Union(NameTable.Empty));
        Assert.Same(table, NameTable.Empty.Union(table));
    }

    private static NameTable Table(params (string Hash, string Path)[] rows)
    {
        var map = new Dictionary<ulong, string>();
        foreach (var (hash, path) in rows) map[PathHashing.Compute(hash)] = path;
        return NameTable.FromMap(map);
    }
}
