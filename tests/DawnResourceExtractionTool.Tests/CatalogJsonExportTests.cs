using System.Text.Json.Nodes;
using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

/// <summary>
/// 按分类导出时把客户端原始数据表转写进导出根目录：只留这次导出用得到的记录、丢掉空字段、
/// 把资源路径改成导出后的相对路径。
/// </summary>
public sealed class CatalogJsonExportTests
{
    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), "dawn-tests", Guid.NewGuid().ToString("N"));

    private static CatalogEntry Entry(ulong hash, string path, CatalogSection section = CatalogSection.Skin)
        => new(hash, path, @"D:\x\default-assets2.wad", section, 0, 0, 1, 1);

    private static CatalogTables Tables(params (string File, string Json)[] files)
        => CatalogTables.FromJson(files.ToDictionary(file => file.File, file => file.Json, StringComparer.OrdinalIgnoreCase));

    private static JsonNode Read(string root, string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, name)))!;

    [Fact]
    public void SkinExportTranscribesItsFourTables()
    {
        var tables = Tables(
            ("skins.json", """
                {
                  "103027": {
                    "id": 103027,
                    "name": "Spirit Blossom Ahri",
                    "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin27/Images/ahri_splash_centered_27.jpg",
                    "skinType": "",
                    "splashVideoPath": null,
                    "skinLines": [ { "id": 171 } ]
                  },
                  "103028": {
                    "id": 103028,
                    "name": "Not Exported",
                    "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin28/Images/ahri_splash_centered_28.jpg"
                  }
                }
                """),
            ("skinlines.json", """
                [
                  { "id": 0, "name": "", "description": "" },
                  { "id": 171, "name": "Spirit Blossom" },
                  { "id": 172, "name": "Other" }
                ]
                """),
            ("universes.json", """
                [
                  { "id": 0, "name": "", "description": "", "imagePath": "", "skinSets": [] },
                  { "id": 37, "name": "Spirit Blossom", "imagePath": "", "skinSets": [ 171 ] },
                  { "id": 38, "name": "Other", "imagePath": "", "skinSets": [ 172 ] }
                ]
                """),
            ("skinborders.json", """
                [
                  {
                    "name": "Spirit Blossom Ahri Border",
                    "itemId": 5,
                    "contentId": "ccedf22a",
                    "image": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin27/UI/ahri_border.png"
                  },
                  {
                    "name": "Some Other Border",
                    "itemId": 6,
                    "contentId": "ddeef33b",
                    "image": "/lol-game-data/assets/ASSETS/Characters/Kayle/Skins/Skin78/UI/kayle_border.png"
                  }
                ]
                """));

        var entries = new[]
        {
            Entry(1, "plugins/rcp-be-lol-game-data/global/default/assets/characters/ahri/skins/skin27/images/ahri_splash_centered_27.jpg"),
            Entry(2, "plugins/rcp-be-lol-game-data/global/default/assets/characters/ahri/skins/skin27/ui/ahri_border.png"),
        };
        var destinations = new Dictionary<ulong, string>
        {
            [1] = @"Skins\Spirit_Blossom\ahri_skin27\assets\characters\ahri\skins\skin27\images\ahri_splash_centered_27.jpg",
            [2] = @"Skins\Spirit_Blossom\ahri_skin27\assets\characters\ahri\skins\skin27\ui\ahri_border.png",
        };

        var root = TempRoot();
        var written = CatalogJsonExport.Write(root, tables, entries, destinations);

        Assert.Equal(new[] { "skins.json", "skinlines.json", "universes.json", "skinborders.json" },
            written.Select(file => file.Table));

        var skins = Read(root, "skins.json").AsObject();
        var skin = Assert.Single(skins).Value!.AsObject();
        Assert.Equal("Spirit Blossom Ahri", skin["name"]!.GetValue<string>());
        Assert.Equal(
            "Skins/Spirit_Blossom/ahri_skin27/assets/characters/ahri/skins/skin27/images/ahri_splash_centered_27.jpg",
            skin["splashPath"]!.GetValue<string>());
        Assert.False(skin.ContainsKey("skinType"));          // 空字符串丢掉
        Assert.False(skin.ContainsKey("splashVideoPath"));   // null 丢掉

        var lines = Read(root, "skinlines.json").AsArray();
        Assert.Equal(171, Assert.Single(lines)!["id"]!.GetValue<int>());

        var universes = Read(root, "universes.json").AsArray();
        var universe = Assert.Single(universes)!.AsObject();
        Assert.Equal(37, universe["id"]!.GetValue<int>());
        Assert.False(universe.ContainsKey("imagePath"));     // 全空的 imagePath 不留

        var border = Assert.Single(Read(root, "skinborders.json").AsArray())!.AsObject();
        Assert.Equal("Skins/Spirit_Blossom/ahri_skin27/assets/characters/ahri/skins/skin27/ui/ahri_border.png",
            border["image"]!.GetValue<string>());
    }

    [Fact]
    public void ChampionExportDropsThePlaceholderRecord()
    {
        var tables = Tables(("champion-summary.json", """
            [
              { "id": -1, "name": "None", "alias": "None", "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/-1.png" },
              { "id": 103, "name": "Ahri", "alias": "Ahri", "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/103.png" },
              { "id": 84, "name": "Akali", "alias": "Akali", "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/84.png" }
            ]
            """));

        var entries = new[] { Entry(1, "assets/characters/ahri/ahri.skn", CatalogSection.Champion) };
        var destinations = new Dictionary<ulong, string> { [1] = @"Champions\ahri\base\ahri.skn" };

        var root = TempRoot();
        CatalogJsonExport.Write(root, tables, entries, destinations);

        var records = Read(root, "champion-summary.json").AsArray();
        var champion = Assert.Single(records)!.AsObject();
        Assert.Equal(103, champion["id"]!.GetValue<int>());
        Assert.Equal("Ahri", champion["alias"]!.GetValue<string>());
    }

    [Fact]
    public void ItemExportKeepsOnlyTheExportedIcons()
    {
        var tables = Tables(("items.json", """
            {
              "1001": { "id": 1001, "name": "Boots", "iconPath": "/lol-game-data/assets/ASSETS/Items/Icons2D/1001.png" },
              "1002": { "id": 1002, "name": "Not Exported", "iconPath": "/lol-game-data/assets/ASSETS/Items/Icons2D/1002.png" }
            }
            """));

        var entries = new[] { Entry(1, "plugins/rcp-be-lol-game-data/global/default/assets/items/icons2d/1001.png", CatalogSection.Item) };
        var destinations = new Dictionary<ulong, string> { [1] = @"Items\Boots\1001\assets\items\icons2d\1001.png" };

        var root = TempRoot();
        var written = CatalogJsonExport.Write(root, tables, entries, destinations);

        var file = Assert.Single(written);
        Assert.Equal("items.json", file.Table);
        var item = Assert.Single(Read(root, "items.json").AsObject()).Value!.AsObject();
        Assert.Equal("Boots", item["name"]!.GetValue<string>());
        Assert.Equal("Items/Boots/1001/assets/items/icons2d/1001.png", item["iconPath"]!.GetValue<string>());
    }

    [Fact]
    public void SeveralExportsIntoOneFolderShareTheSameTables()
    {
        var first = new[] { Entry(1, "assets/characters/ahri/skins/skin27/images/ahri_splash_centered_27.jpg") };
        var second = new[] { Entry(2, "assets/characters/lux/skins/skin05/images/lux_splash_centered_05.jpg") };
        var root = TempRoot();

        var firstTables = Tables(
            ("skins.json", """{ "103027": { "id": 103027, "name": "Spirit Blossom Ahri", "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin27/Images/ahri_splash_centered_27.jpg", "skinLines": [ { "id": 171 } ] } }"""),
            ("skinlines.json", """[ { "id": 171, "name": "Spirit Blossom" } ]"""));
        var secondTables = Tables(
            ("skins.json", """{ "105005": { "id": 105005, "name": "Lux Skin 05", "splashPath": "/lol-game-data/assets/ASSETS/Characters/Lux/Skins/Skin05/Images/lux_splash_centered_05.jpg", "skinLines": [ { "id": 172 } ] } }"""),
            ("skinlines.json", """[ { "id": 171, "name": "Spirit Blossom" }, { "id": 172, "name": "Other" } ]"""));

        CatalogJsonExport.Write(root, firstTables, first,
            new Dictionary<ulong, string> { [1] = @"Skins\Spirit_Blossom\ahri_skin27\assets\characters\ahri\skins\skin27\images\ahri_splash_centered_27.jpg" });
        CatalogJsonExport.Write(root, secondTables, second,
            new Dictionary<ulong, string> { [2] = @"Skins\Other\lux_skin05\assets\characters\lux\skins\skin05\images\lux_splash_centered_05.jpg" });

        // 两次导出落到同一个目录：表里两条记录都在，按 id 排好。
        var skins = Read(root, "skins.json").AsObject();
        Assert.Equal(new[] { "103027", "105005" }, skins.Select(pair => pair.Key));
        var lines = Read(root, "skinlines.json").AsArray();
        Assert.Equal(new[] { 171, 172 }, lines.Select(node => node!["id"]!.GetValue<int>()));
    }

    [Fact]
    public void SkinExportStillWritesATableWithNoMatchingRecord()
    {
        var tables = Tables(
            ("skins.json", """{ "1001": { "id": 1001, "name": "Elsewhere", "splashPath": "/lol-game-data/assets/ASSETS/Characters/x/y.jpg" } }"""),
            ("skinlines.json", """[ { "id": 171, "name": "Spirit Blossom" } ]"""));
        var entries = new[] { Entry(1, "assets/characters/ahri/skins/skin27/ahri.skn") };
        var root = TempRoot();

        var written = CatalogJsonExport.Write(root, tables, entries, new Dictionary<ulong, string> { [1] = "Skins/x/x.skn" });

        Assert.Equal(new[] { "skins.json", "skinlines.json" }, written.Select(file => file.Table));
        Assert.All(written, file => Assert.Equal(0, file.Records));
        Assert.Empty(Read(root, "skins.json").AsObject());
        Assert.Empty(Read(root, "skinlines.json").AsArray());
    }
}
