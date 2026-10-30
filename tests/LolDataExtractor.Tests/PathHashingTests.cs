using System.Text;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class PathHashingTests
{
    // Golden pairs taken from the CommunityDragon hashtable (hash -> path).
    [Theory]
    [InlineData("assets/characters/tft10_8bitprop/hud/icons2d/tft10_8bitprop_eyeofthestorm.dds", 0x9da598ed5567c561UL)]
    [InlineData("assets/characters/tft10_8bitprop/hud/icons2d/tft10_8bitprop_eyeofthestorm.tft_set10.dds", 0x762f5e8f466f6405UL)]
    public void Computes_the_same_hash_as_the_community_hashtable(string path, ulong expected)
        => Assert.Equal(expected, PathHashing.Compute(path));

    [Fact]
    public void Hashing_is_case_insensitive_because_paths_are_lowercased()
    {
        var upper = PathHashing.Compute("ASSETS/Characters/Ahri/HUD/Ahri_Circle_0.dds");
        var lower = PathHashing.Compute("assets/characters/ahri/hud/ahri_circle_0.dds");
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void Renders_twelve_character_lowercase_hex()
    {
        var text = PathHashing.ToText(0x00FF00FF00FF00FFUL);
        Assert.Equal(16, text.Length);
        Assert.Equal("00ff00ff00ff00ff", text);
    }

    [Fact]
    public void Hash_matches_an_entry_of_a_real_wad_when_the_fixture_exists()
    {
        var wadPath = Path.Combine(TestPaths.TestDataDirectory, "Ahri.wad.client");
        if (!File.Exists(wadPath)) return; // 大文件副本不入库，缺失时跳过

        using var archive = WadArchive.Open(wadPath);
        var hashes = archive.ReadEntries(null).Select(e => e.Hash).ToHashSet();
        Assert.Contains(PathHashing.Compute("assets/characters/ahri/skins/base/ahri_base.skn"), hashes);
        Assert.Contains(PathHashing.Compute("assets/characters/ahri/skins/base/ahri_base.skl"), hashes);
    }
}

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string TestDataDirectory => Path.Combine(RepositoryRoot, "tests", "testdata");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }
}
