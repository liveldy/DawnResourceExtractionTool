using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

public class ExtractionFilterTests
{
    private static WadEntryInfo Entry(string? path, long size = 1024, ulong? hash = null)
        => new(hash ?? (path is null ? 42UL : PathHashing.Compute(path)), path, size, (int)size, "ZStandardChunked", false);

    [Fact]
    public void Empty_filter_matches_everything()
    {
        var filter = new ExtractionFilter();
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(Entry("assets/a.dds")));
        Assert.True(filter.Matches(Entry(null)));
    }

    [Theory]
    [InlineData("*skins/base/*", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("*skins/base/*", "assets/characters/ahri/skins/skin01/ahri.skn", false)]
    [InlineData("*.bnk", "assets/sounds/wwise2016/base.bnk", true)]
    [InlineData("*.bnk", "assets/sounds/wwise2016/base.wem", false)]
    [InlineData("assets/characters/ahri/*", "assets/characters/ahri/hud/x.tex", true)]
    [InlineData("hud", "assets/characters/ahri/hud/x.tex", true)]
    public void Glob_matching_is_case_insensitive_and_helper_wraps_plain_text(string pattern, string path, bool expected)
    {
        var filter = new ExtractionFilter { PathPattern = pattern.Contains('*') ? pattern : $"*{pattern}*" };
        Assert.Equal(expected, filter.Matches(Entry(path)));
    }

    [Fact]
    public void Filters_by_category_and_extension()
    {
        var filter = new ExtractionFilter
        {
            Categories = new HashSet<string> { "model" },
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "skn", "skl" },
        };

        Assert.True(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base.skn")));
        Assert.False(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base.tex")));
    }

    [Fact]
    public void Filters_by_size_window()
    {
        var filter = new ExtractionFilter { MinSize = 100, MaxSize = 200 };
        Assert.False(filter.Matches(Entry("assets/a.bin", 99)));
        Assert.True(filter.Matches(Entry("assets/a.bin", 150)));
        Assert.False(filter.Matches(Entry("assets/a.bin", 201)));
    }

    [Fact]
    public void Can_exclude_unnamed_entries()
    {
        var filter = new ExtractionFilter { IncludeUnnamed = false };
        Assert.False(filter.Matches(Entry(null)));
        Assert.True(filter.Matches(Entry("assets/a.bin")));
    }

    [Fact]
    public void Matches_unnamed_entries_by_hash_text()
    {
        var filter = new ExtractionFilter { PathPattern = "000000000000002a" };
        Assert.True(filter.Matches(Entry(null, hash: 42)));
    }

    [Theory]
    [InlineData("assets/characters/ahri", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("assets/characters/ahri/", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("\\assets\\characters\\ahri\\", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("ASSETS/characters/Ahri", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("assets/characters/ahri", "assets/characters/ahrix/skins/base/ahri_base.skn", false)]
    [InlineData("assets/characters/ahri", "assets/characters/akali/skins/base/akali_base.skn", false)]
    [InlineData("  ", "assets/characters/ahri/skins/base/ahri_base.skn", true)]
    public void Filters_by_folder_prefix(string prefix, string path, bool expected)
    {
        var filter = new ExtractionFilter { PathPrefix = prefix };
        Assert.Equal(expected, filter.Matches(Entry(path)));
    }

    [Fact]
    public void A_folder_prefix_only_matches_named_entries()
    {
        var filter = new ExtractionFilter { PathPrefix = "assets/characters/ahri" };
        Assert.False(filter.Matches(Entry(null)));
        Assert.False(filter.IsEmpty);
    }

    [Fact]
    public void Folder_prefixes_are_normalised()
    {
        Assert.Equal("assets/ahri/", ExtractionFilter.NormalizePrefix("\\assets\\ahri\\"));
        Assert.Null(ExtractionFilter.NormalizePrefix("   "));
        Assert.Null(ExtractionFilter.NormalizePrefix("/"));
    }

    [Fact]
    public void Combines_a_folder_prefix_with_a_category()
    {
        var filter = new ExtractionFilter
        {
            PathPrefix = "assets/characters/ahri",
            Categories = new HashSet<string> { "texture" },
        };

        Assert.True(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base_tx_cm.tex")));
        Assert.False(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base.skn")));
        Assert.False(filter.Matches(Entry("assets/characters/akali/skins/base/akali_tx_cm.tex")));
    }

    [Theory]
    [InlineData("assets/characters/ahri/skins/base/ahri_base.skn", true)]
    [InlineData("\\assets\\characters\\ahri\\skins\\base\\ahri_base.skn", true)]
    [InlineData("ASSETS/Characters/Ahri/Skins/Base/Ahri_Base.skn", true)]
    [InlineData("assets/characters/ahri/skins/base/ahri_base.skn.bak", false)]
    [InlineData("assets/characters/ahri/skins/base/ahri_base.tex", false)]
    public void Exact_paths_keep_only_the_picked_entries(string path, bool expected)
    {
        var filter = new ExtractionFilter
        {
            ExactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "assets/characters/ahri/skins/base/ahri_base.skn",
            },
        };

        Assert.Equal(expected, filter.Matches(Entry(path)));
        Assert.False(filter.IsEmpty);
    }

    [Fact]
    public void Exact_paths_never_match_unnamed_entries()
    {
        var filter = new ExtractionFilter
        {
            ExactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "assets/a.bin" },
        };

        Assert.False(filter.Matches(Entry(null)));
    }

    [Fact]
    public void Exact_hashes_keep_only_the_ticked_entries()
    {
        var first = PathHashing.Compute("assets/characters/ahri/skins/base/ahri_base.skn");
        var unnamed = 0xabcdef0123456789UL;
        var filter = new ExtractionFilter { ExactHashes = new HashSet<ulong> { first, unnamed } };

        Assert.True(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base.skn")));
        Assert.True(filter.Matches(Entry(null, hash: unnamed)));
        Assert.False(filter.Matches(Entry("assets/characters/ahri/skins/base/ahri_base.tex")));
        Assert.False(filter.IsEmpty);
    }
}

public class WadEntryInfoTests
{
    [Fact]
    public void Derives_extension_category_and_file_name()
    {
        var entry = new WadEntryInfo(1, "assets/characters/ahri/hud/ahri_circle_0.dds", 100, 50, "Uncompressed", false);
        Assert.Equal("dds", entry.Extension);
        Assert.Equal("texture", entry.Category);
        Assert.Equal("ahri_circle_0.dds", entry.FileName);
        Assert.True(entry.IsNamed);
    }

    [Fact]
    public void Unnamed_entries_fall_back_to_hash_names()
    {
        var entry = new WadEntryInfo(0xabcdef0123456789, null, 100, 50, "Uncompressed", false);
        Assert.False(entry.IsNamed);
        Assert.Equal("abcdef0123456789", entry.FileName);
        Assert.Equal(string.Empty, entry.Extension);
        Assert.Equal("other", entry.Category);
    }
}
