using DawnResourceExtractionTool.Core;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Tests;

/// <summary>
/// The client ships its json once per language folder, so an export has to pick a copy: the one of the
/// language the window is in, the English <c>default</c> copy when that language has none.
/// </summary>
public class CatalogLanguageTests
{
    private const string Champion = "plugins/rcp-be-lol-game-data/global/{0}/v1/champions/103.json";

    private static CatalogEntry Entry(string path)
        => new(PathHashing.Compute(path), path, @"D:\wads\x.wad.client", CatalogSection.Champion, 0, 0, 100, 1);

    private static CatalogEntry English() => Entry(string.Format(Champion, CatalogLanguages.EnglishFolder));

    private static CatalogEntry Chinese() => Entry(string.Format(Champion, CatalogLanguages.ChineseFolder));

    private static CatalogEntry GameFile() => Entry("assets/characters/ahri/skins/skin00.bin");

    [Fact]
    public void Language_folder_is_read_from_the_client_path()
    {
        Assert.Equal("default", CatalogLanguages.FolderOfPath(string.Format(Champion, "default")));
        Assert.Equal("zh_cn", CatalogLanguages.FolderOfPath(string.Format(Champion, "zh_cn")));
        Assert.Equal("ko_kr", CatalogLanguages.FolderOfPath(string.Format(Champion, "ko_kr")));

        // 游戏 wad 的路径没有语言段，别的插件也不是这个形状。
        Assert.Equal(string.Empty, CatalogLanguages.FolderOfPath("assets/characters/ahri/skins/skin00.bin"));
        Assert.Equal(string.Empty, CatalogLanguages.FolderOfPath(null));
        Assert.Equal(string.Empty, CatalogLanguages.FolderOfPath("plugins/rcp-be-lol-license-agreement/br/pt_br/x.txt"));
    }

    [Fact]
    public void Both_language_copies_are_the_same_file()
    {
        Assert.Equal(CatalogLanguages.LogicalOfPath(English().Path), CatalogLanguages.LogicalOfPath(Chinese().Path));
        Assert.Equal("plugins/rcp-be-lol-game-data/global/v1/champions/103.json",
            CatalogLanguages.LogicalOfPath(Chinese().Path));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese, "zh_cn")]
    [InlineData(AppLanguage.English, "default")]
    public void Export_keeps_the_copy_of_the_language(AppLanguage language, string expected)
    {
        var kept = CatalogLanguages.PickForExport(new[] { English(), Chinese() }, CatalogLanguages.FolderOf(language));

        var entry = Assert.Single(kept);
        Assert.Equal(expected, CatalogLanguages.FolderOfPath(entry.Path));
    }

    [Fact]
    public void Export_falls_back_to_english_when_the_language_has_no_copy()
    {
        var kept = CatalogLanguages.PickForExport(new[] { English() }, CatalogLanguages.ChineseFolder);
        Assert.Equal(CatalogLanguages.EnglishFolder, CatalogLanguages.FolderOfPath(Assert.Single(kept).Path));

        // 连英文那份都没有时，别的语言的副本仍然导出，而不是把文件整个丢掉。
        var only = Entry(string.Format(Champion, "ko_kr"));
        Assert.Equal("ko_kr", CatalogLanguages.FolderOfPath(Assert.Single(CatalogLanguages.PickForExport(
            new[] { only }, CatalogLanguages.ChineseFolder)).Path));
    }

    [Fact]
    public void Export_leaves_files_without_a_language_alone()
    {
        var kept = CatalogLanguages.PickForExport(
            new[] { GameFile(), English(), Chinese(), GameFile() }, CatalogLanguages.ChineseFolder);

        Assert.Equal(3, kept.Count);
        Assert.Equal(2, kept.Count(entry => entry.Path.StartsWith("assets/", StringComparison.Ordinal)));
        Assert.Equal(CatalogLanguages.ChineseFolder, CatalogLanguages.FolderOfPath(kept[1].Path));
    }

    [Fact]
    public void Export_follows_the_language_of_the_window()
    {
        var original = Strings.Language;
        try
        {
            Strings.Language = AppLanguage.English;
            Assert.Equal(CatalogLanguages.EnglishFolder, CatalogLanguages.CurrentFolder);
            Assert.Equal(CatalogLanguages.EnglishFolder,
                CatalogLanguages.FolderOfPath(Assert.Single(CatalogLanguages.PickForExport(new[] { Chinese(), English() })).Path));

            Strings.Language = AppLanguage.Chinese;
            Assert.Equal(CatalogLanguages.ChineseFolder,
                CatalogLanguages.FolderOfPath(Assert.Single(CatalogLanguages.PickForExport(new[] { Chinese(), English() })).Path));
        }
        finally
        {
            Strings.Language = original;
        }
    }

    [Fact]
    public void Table_text_follows_the_language_of_the_window()
    {
        var original = Strings.Language;
        try
        {
            var english = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["champion-summary.json"] = "[{\"id\":103,\"name\":\"Ahri\"}]",
            };
            var chinese = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["champion-summary.json"] = "[{\"id\":103,\"name\":\"九尾妖狐\"}]",
            };
            var byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogLanguages.EnglishFolder] = english,
                [CatalogLanguages.ChineseFolder] = chinese,
            };

            var tables = CatalogTables.FromJson(english, byLanguage);

            Strings.Language = AppLanguage.English;
            Assert.True(tables.TryGetTableText("champion-summary.json", out var text));
            Assert.Contains("Ahri", text);

            Strings.Language = AppLanguage.Chinese;
            Assert.True(tables.TryGetTableText("champion-summary.json", out text));
            Assert.Contains("九尾妖狐", text);

            // 没有本地化副本的表照样能转写：回落到读到的那份。
            var only = CatalogTables.FromJson(english);
            Assert.True(only.TryGetTableText("champion-summary.json", out var fallback));
            Assert.Contains("Ahri", fallback);
        }
        finally
        {
            Strings.Language = original;
        }
    }
}
