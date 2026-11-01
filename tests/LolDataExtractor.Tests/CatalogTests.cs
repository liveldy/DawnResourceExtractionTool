using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>
/// The catalog is the only place where folder names are guessed at, so the rules that decide
/// "this belongs to Ahri" and "this is the series of that skin" are pinned here. The shapes come
/// from the real client tables.
/// </summary>
public class CatalogTests
{
    /// <summary>The English table, in the shape the client writes it.</summary>
    private const string ChampionSummary = """
        [
          { "id": -1, "name": "None", "description": "", "alias": "None" },
          { "id": 103, "name": "Ahri", "description": "the Nine-Tailed Fox", "alias": "Ahri",
            "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/103.png" },
          { "id": 1, "name": "Annie", "description": "the Dark Child", "alias": "Annie",
            "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/1.png" },
          { "id": 238, "name": "Zed", "description": "the Master of Shadows", "alias": "Zed",
            "squarePortraitPath": "/lol-game-data/assets/v1/champion-icons/238.png" }
        ]
        """;

    private const string Skins = """
        {
          "103000": {
            "id": 103000, "isBase": true, "name": "Ahri", "description": "",
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Base/Images/ahri_splash_centered_0.jpg",
            "chromas": []
          },
          "103005": {
            "id": 103005, "isBase": false, "name": "Challenger Ahri", "description": "A champion of the rift.",
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/ahri_splash_centered_5.jpg",
            "tilePath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/ahri_splash_tile_5.jpg",
            "skinLines": [{ "id": 110 }],
            "chromas": [
              { "id": 103018, "name": "Challenger Ahri (Golden)",
                "chromaPath": "/lol-game-data/assets/v1/champion-chroma-images/103/103018.png",
                "tilePath": "/lol-game-data/assets/v1/champion-chroma-images/103/103018.png" }
            ]
          },
          "1001": {
            "id": 1001, "isBase": false, "name": "Goth Annie", "description": "",
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Annie/Skins/Skin01/Images/annie_splash_centered_1.jpg",
            "skinLines": [{ "id": 110 }],
            "chromas": []
          },
          "103001": {
            "id": 103001, "isBase": false, "name": "Goth Ahri", "description": "",
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin01/Images/ahri_splash_centered_1.jpg",
            "skinLines": [{ "id": 110 }],
            "chromas": []
          }
        }
        """;

    private const string SkinLines = """
        [
          { "id": 0, "name": "", "description": "" },
          { "id": 110, "name": "Goth", "description": "It's not a phase." }
        ]
        """;

    private const string Universes = """
        [
          { "id": 0, "name": "", "description": "", "imagePath": "", "skinSets": [] },
          { "id": 100, "name": "Neighborhood Nightmare", "description": "", "imagePath": "",
            "skinSets": [110] }
        ]
        """;

    private const string Items = """
        [
          { "id": 1001, "name": "Boots", "description": "<mainText><stats><attention>25</attention> Move Speed</stats></mainText>",
            "categories": ["Boots"], "iconPath": "/lol-game-data/assets/ASSETS/Items/Icons2D/1001_class_t1_bootsofspeed.png" },
          { "id": 1004, "name": "Faerie Charm", "description": "",
            "categories": ["ManaRegen", "GoldPer"], "iconPath": "/lol-game-data/assets/ASSETS/Items/Icons2D/1004_class_t1_faeriecharm.png" }
        ]
        """;

    private const string Perks = """
        [
          { "id": 8128, "name": "Dark Harvest", "shortDesc": "Damaging a low health champion harvests their soul.",
            "iconPath": "/lol-game-data/assets/v1/perk-images/Styles/Domination/DarkHarvest/DarkHarvest.png" },
          { "id": 8437, "name": "Grasp of the Undying", "shortDesc": "",
            "iconPath": "/lol-game-data/assets/v1/perk-images/Styles/Resolve/GraspOfTheUndying/GraspOfTheUndying.png" }
        ]
        """;

    private const string PerkStyles = """
        {
          "schemaVersion": 3,
          "styles": [
            { "id": 8100, "name": "Domination", "tooltip": "Burst damage and target access",
              "iconPath": "/lol-game-data/assets/v1/perk-images/Styles/7200_Domination.png",
              "assetMap": { "p8100_s0_k0": "/lol-game-data/assets/v1/perk-images/Styles/Domination/p8100_s0_k0.jpg" },
              "slots": [{ "type": "kKeyStone", "perks": [8128] }] },
            { "id": 8400, "name": "Resolve", "tooltip": "Durability and crowd control",
              "iconPath": "/lol-game-data/assets/v1/perk-images/Styles/7204_Resolve.png",
              "assetMap": {},
              "slots": [{ "type": "kKeyStone", "perks": [8437] }] }
          ]
        }
        """;

    private const string Regalia = """
        [
          { "id": "3", "assetPath": "/lol-game-data/assets/ASSETS/Regalia/BannerSkins/lny2023.png",
            "regaliaType": "kBanner", "localizedName": "Lunar Revel 2023 Banner", "localizedDescription": "" }
        ]
        """;

    private const string Emotes = """
        [
          { "id": 10, "name": "Mastery 10+", "inventoryIcon": "/lol-game-data/assets/", "description": "" },
          { "id": 11, "name": "Happy", "inventoryIcon": "/lol-game-data/assets/ASSETS/Loadouts/SummonerEmotes/Champions/Ahri/ahri_happy_inventory.png",
            "description": "A happy emote." }
        ]
        """;

    private const string Icons = """
        [
          { "id": 10001, "title": "Prestige Icon", "imagePath": "/lol-game-data/assets/v1/profile-icons/10001.jpg",
            "descriptions": [{ "region": "riot", "description": "Prestige collection icon" }] },
          { "id": 20002, "title": "Loose Icon", "imagePath": "/lol-game-data/assets/v1/profile-icons/20002.jpg",
            "descriptions": [] }
        ]
        """;

    private const string IconSets = """
        [
          { "id": 100, "displayName": "Special Events", "description": "", "icons": [10001] }
        ]
        """;

    private const string Spells = """
        [
          { "id": 1, "name": "Cleanse", "description": "Removes disables.",
            "gameModes": ["CLASSIC", "ARAM"], "iconPath": "/lol-game-data/assets/DATA/Spells/Icons2D/Summoner_boost.png" }
        ]
        """;

    private const string Wards = """
        [
          { "id": 1, "name": "Harrowing Ward", "description": "A spooky ward.",
            "wardImagePath": "/lol-game-data/assets/content/src/LeagueClient/WardSkinImages/wardHero_1.png",
            "wardShadowImagePath": "/lol-game-data/content/src/LeagueClient/WardSkinImages/wardHeroShadow_1.png" }
        ]
        """;

    private const string WardSets = """
        [
          { "id": 10, "displayName": "Harrowing", "description": "", "wards": [1] }
        ]
        """;

    private const string SkinBorders = """
        [
          { "name": "Challenger Border", "image": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/UI/ahri_border_5.png" }
        ]
        """;

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["champion-summary.json"] = ChampionSummary,
        ["skins.json"] = Skins,
        ["skinlines.json"] = SkinLines,
        ["universes.json"] = Universes,
        ["skinborders.json"] = SkinBorders,
        ["items.json"] = Items,
        ["perks.json"] = Perks,
        ["perkstyles.json"] = PerkStyles,
        ["regalia.json"] = Regalia,
        ["summoner-emotes.json"] = Emotes,
        ["summoner-icons.json"] = Icons,
        ["summoner-icon-sets.json"] = IconSets,
        ["summoner-spells.json"] = Spells,
        ["ward-skins.json"] = Wards,
        ["ward-skin-sets.json"] = WardSets,
    };

    private static CatalogTables Tables() => CatalogTables.FromJson(Files());

    /// <summary>Overrides of a throwaway file, so a test never touches the user's own.</summary>
    private static CatalogOverrides Overrides() => CatalogOverrides.Load(TempFile());

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), "loldata-catalog-tests", Guid.NewGuid().ToString("N") + ".json");

    // ── tables ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadsChampionsAndSkins()
    {
        var tables = Tables();

        Assert.Equal(3, tables.Champions.Count);      // id -1 is not a champion
        Assert.Equal(4, tables.Skins.Count);          // skins.json is an object keyed by id
        Assert.True(tables.TryGetChampionByAlias("ahri", out var ahri));
        Assert.Equal(103, ahri.Id);
        Assert.Equal("Ahri", ahri.Label);             // the alias, never the localized name
        Assert.Equal("Ahri · the Nine-Tailed Fox", CatalogTables.DetailOf(ahri));

        Assert.True(tables.TryGetSkin(103005, out var skin));
        Assert.Equal("Challenger Ahri", skin.Name);
        Assert.Equal(103, skin.ChampionId);
        Assert.Equal(110, skin.SkinLine);
    }

    [Fact]
    public void SkinSeriesJoinUniverseAndLine()
    {
        var tables = Tables();
        Assert.True(tables.TryGetSkin(103005, out var skin));

        var (label, detail, rank) = tables.SeriesOf(skin);
        Assert.Equal("Neighborhood Nightmare · Goth", label);
        Assert.Equal("It's not a phase.", detail);
        Assert.Equal(0, rank);

        Assert.True(tables.TryGetSkin(103000, out var baseSkin));
        Assert.Equal("Base", tables.SeriesOf(baseSkin).Label);
    }

    [Fact]
    public void SameSkinNumberOfTwoChampionsStaysApart()
    {
        var tables = Tables();
        var nodes = new CatalogNodes();
        Assert.True(tables.TryGetSkin(1001, out var annie));
        Assert.True(tables.TryGetSkin(103001, out var ahri));

        var first = tables.SkinTarget(nodes, annie);
        var second = tables.SkinTarget(nodes, ahri);

        // Same series group and both live in "skin01", but they are two different skins.
        Assert.Equal(first.Group, second.Group);
        Assert.NotEqual(first.Entity, second.Entity);
        Assert.Equal("annie_skin01", nodes.Entities[first.Entity].Folder);
        Assert.Equal("ahri_skin01", nodes.Entities[second.Entity].Folder);
    }

    [Fact]
    public void BrokenTablesOnlyCostLabels()
    {
        var tables = CatalogTables.FromJson(new Dictionary<string, string> { ["champion-summary.json"] = "{ not json" });

        Assert.True(tables.IsEmpty);
        Assert.False(tables.TryGuessChampion("ahri", out _, out _));
    }

    [Fact]
    public void ReadsItemRuneIconAndWardGroups()
    {
        var tables = Tables();
        var nodes = new CatalogNodes();
        var index = tables.BuildReferenceIndex(nodes);

        var item = Resolve(index, nodes, "/lol-game-data/assets/ASSETS/Items/Icons2D/1001_class_t1_bootsofspeed.png");
        Assert.Equal(CatalogSection.Item, item.Group.Section);
        Assert.Equal("Boots", item.Group.Label);
        Assert.Equal("Boots", item.Entity.Label);
        Assert.Equal("25 Move Speed", item.Entity.Detail);   // markup is dropped

        var perk = Resolve(index, nodes, "/lol-game-data/assets/v1/perk-images/Styles/Domination/DarkHarvest/DarkHarvest.png");
        Assert.Equal(CatalogSection.Perk, perk.Group.Section);
        Assert.Equal("Domination", perk.Group.Label);
        Assert.Equal("Dark Harvest", perk.Entity.Label);

        var icon = Resolve(index, nodes, "/lol-game-data/assets/v1/profile-icons/10001.jpg");
        Assert.Equal(CatalogSection.Icon, icon.Group.Section);
        Assert.Equal("Special Events", icon.Group.Label);
        Assert.Equal("Prestige Icon", icon.Entity.Label);

        var loose = Resolve(index, nodes, "/lol-game-data/assets/v1/profile-icons/20002.jpg");
        Assert.Equal("Other", loose.Group.Label);

        var emote = Resolve(index, nodes, "/lol-game-data/assets/ASSETS/Loadouts/SummonerEmotes/Champions/Ahri/ahri_happy_inventory.png");
        Assert.Equal(CatalogSection.Emote, emote.Group.Section);
        Assert.Equal("Champions", emote.Group.Label);

        var ward = Resolve(index, nodes, "/lol-game-data/assets/content/src/LeagueClient/WardSkinImages/wardHero_1.png");
        Assert.Equal(CatalogSection.Ward, ward.Group.Section);
        Assert.Equal("Harrowing", ward.Group.Label);

        var spell = Resolve(index, nodes, "/lol-game-data/assets/DATA/Spells/Icons2D/Summoner_boost.png");
        Assert.Equal(CatalogSection.SummonerSpell, spell.Group.Section);
        Assert.Equal("Cleanse", spell.Entity.Label);
    }

    private static (CatalogGroup Group, CatalogEntity Entity) Resolve(
        IReadOnlyDictionary<ulong, CatalogPlacement> index, CatalogNodes nodes, string reference)
    {
        var placement = CatalogPaths.ClientCandidates(reference)
            .Select(PathHashing.Compute)
            .Where(index.ContainsKey)
            .Select(hash => index[hash])
            .First();
        return (nodes.Groups[placement.Group], nodes.Entities[placement.Entity]);
    }

    [Fact]
    public void SkinReferencesLandInTheirSeries()
    {
        var tables = Tables();
        var nodes = new CatalogNodes();
        var index = tables.BuildReferenceIndex(nodes);

        var splash = Resolve(index, nodes,
            "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/ahri_splash_centered_5.jpg");
        Assert.Equal(CatalogSection.Skin, splash.Group.Section);
        Assert.Equal("Neighborhood Nightmare · Goth", splash.Group.Label);
        Assert.Equal("Challenger Ahri", splash.Entity.Label);
        Assert.Equal("ahri_skin05", splash.Entity.Folder);
        Assert.Equal("A champion of the rift.", splash.Entity.Detail);

        // A chroma image belongs to the skin that owns the chroma.
        var chroma = Resolve(index, nodes,
            "/lol-game-data/assets/v1/champion-chroma-images/103/103018.png");
        Assert.Equal("Challenger Ahri", chroma.Entity.Label);

        // A skin border lives with the skin it decorates.
        var border = Resolve(index, nodes,
            "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/UI/ahri_border_5.png");
        Assert.Equal("Challenger Ahri", border.Entity.Label);

        // Base skins are grouped together, not with a series.
        var baseSkin = Resolve(index, nodes,
            "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Base/Images/ahri_splash_centered_0.jpg");
        Assert.Equal("Base", baseSkin.Group.Label);
        Assert.Equal("Ahri", baseSkin.Entity.Label);
    }

    [Fact]
    public void ChampionPortraitIsTheChampionIcon()
    {
        var tables = Tables();
        var nodes = new CatalogNodes();
        var index = tables.BuildReferenceIndex(nodes);

        var icon = Resolve(index, nodes, "/lol-game-data/assets/v1/champion-icons/1.png");
        Assert.Equal(CatalogSection.Champion, icon.Group.Section);
        Assert.Equal("Annie", icon.Group.Label);
        Assert.Equal("Icon", icon.Entity.Label);
    }

    [Fact]
    public void EmptyEmoteReferenceIsNotAFile()
    {
        Assert.False(CatalogPaths.LooksLikeFile("/lol-game-data/assets/"));
        Assert.True(CatalogPaths.LooksLikeFile("/lol-game-data/assets/v1/profile-icons/1.jpg"));
    }

    // ── path helpers ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClientCandidatesTryThePathWithAndWithoutTheAssetsPrefix()
    {
        var candidates = CatalogPaths.ClientCandidates("/lol-game-data/assets/v1/champion-icons/103.png");

        Assert.Equal(4, candidates.Count);
        Assert.Contains("plugins/rcp-be-lol-game-data/global/default/assets/v1/champion-icons/103.png", candidates);
        Assert.Contains("plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png", candidates);
        Assert.Contains(candidates, path => path.StartsWith("plugins/rcp-be-lol-game-data/global/zh_cn/", StringComparison.Ordinal));
    }

    [Fact]
    public void ACandidateResolvesToTheEntryOfTheNameTable()
    {
        const string clientPath = "plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png";
        var names = NameTable.FromMap(new Dictionary<ulong, string>
        {
            [PathHashing.Compute(clientPath)] = clientPath,
        });

        var resolved = CatalogPaths.ClientCandidates("/lol-game-data/assets/v1/champion-icons/103.png")
            .Select(PathHashing.Compute)
            .Where(hash => names.IndexOf(hash) >= 0)
            .ToList();

        var hash = Assert.Single(resolved);
        Assert.Equal(PathHashing.Compute(clientPath), hash);
        Assert.Equal(clientPath, names.GetPath(hash));
    }

    [Theory]
    [InlineData("assets/sounds/wwise2016/vo/en_us/characters/ahri/skins/base/ahri_base_vo_audio.bnk", "en_us")]
    [InlineData("assets/characters/ahri/skins/base/ahri_base_tx_cm.tex", "")]
    public void ReadsTheLanguageOfAVoicePath(string path, string expected)
        => Assert.Equal(expected, CatalogPaths.LanguageOf(path));

    [Fact]
    public void RelativePathDropsTheSharedAnchor()
    {
        Assert.Equal(Path.Combine("particles", "ahri_base_tx_cm.tex"),
            CatalogPaths.RelativeOf("assets/characters/ahri/skins/base/particles/ahri_base_tx_cm.tex",
                "assets/characters/ahri/skins/base/"));
        Assert.Equal(Path.Combine("v1", "champion-icons", "103.png"),
            CatalogPaths.RelativeOf("plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png", string.Empty));
    }

    [Fact]
    public void SanitizesNamesWindowsRefuses()
    {
        Assert.Equal("a_b", CatalogPaths.SanitizePath("a:b").Replace(Path.DirectorySeparatorChar, '_'));
        Assert.Equal("unnamed", CatalogPaths.SafeFolderOf("  "));
        Assert.Equal("阿狸_Ahri", CatalogPaths.SafeFolderOf("阿狸 Ahri"));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("item")]
    [InlineData("装备")]
    public void SectionKeysAcceptTheEnumOrTheLabel(string key)
    {
        Assert.True(CatalogSectionInfo.TryParse(key, out var section));
        Assert.Equal(CatalogSection.Item, section);
    }

    [Fact]
    public void ExportPathKeepsSectionGroupAndEntity()
    {
        var section = CatalogSectionInfo.Of(CatalogSection.Champion);
        var group = new CatalogGroup(CatalogSection.Champion, "Ahri", "ahri", null, 0);
        var entity = new CatalogEntity(0, "Challenger Ahri", "skin05", null, 5)
        {
            Anchor = "assets/characters/ahri/skins/skin05/",
        };
        var entry = new CatalogEntry(1, "assets/characters/ahri/skins/skin05/particles/ahri_base_trail_01.tex",
            @"D:\x\Ahri.wad.client", CatalogSection.Champion, 0, 0, 100, 1);

        Assert.Equal(Path.Combine("Champions", "ahri", "skin05", "particles", "ahri_base_trail_01.tex"),
            CatalogPaths.ExportPathOf(entry, section, group, entity));

        // A file outside the entity's anchor keeps the rest of its own folders.
        var loose = entry with { Path = "plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png" };
        Assert.Equal(Path.Combine("Champions", "ahri", "skin05", "v1", "champion-icons", "103.png"),
            CatalogPaths.ExportPathOf(loose, section, group, entity));
    }

    [Fact]
    public void InfoJsonSitsAtTheExportRootAndExplainsEveryEntity()
    {
        var section = CatalogSectionInfo.Of(CatalogSection.Champion);
        var group = new CatalogGroup(CatalogSection.Champion, "Ahri", "ahri", null, 0);
        var entity = new CatalogEntity(0, "Challenger Ahri", "skin05", null, 5)
        {
            Anchor = "assets/characters/ahri/skins/skin05/",
        };

        Assert.Equal(Path.Combine("Champions", "ahri", "skin05"),
            CatalogPaths.EntityFolderOf(section, group, entity));

        var root = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        var folder = CatalogPaths.EntityFolderOf(section, group, entity);
        var file = new CatalogInfoEntry(Path.Combine("particles", "a.tex"),
            "assets/characters/ahri/skins/skin05/particles/a.tex", "0123456789ABCDEF", 100, "Ahri.wad.client", "tex");

        var document = CatalogInfo.Write(root, "LolDataExtractor 1.1.1",
            new[] { new CatalogInfoEntity(section.Display, group.Label, entity.Label, entity.Anchor, folder, 1, 100, new[] { file }) });

        var path = Path.Combine(root, "info.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(root, folder, "info.json")));   // 不再往条目文件夹里塞小 json
        Assert.Equal(1, document.EntityCount);

        var reopened = CatalogInfo.Read(path);
        Assert.NotNull(reopened);
        var only = Assert.Single(reopened!.Entities);
        Assert.Equal("Ahri", only.Group);
        Assert.Equal("Challenger Ahri", only.Entity);
        Assert.Equal(folder, only.Folder);
        var entry = Assert.Single(only.Files);
        Assert.Equal("Ahri.wad.client", entry.Wad);
        Assert.Equal("0123456789ABCDEF", entry.Hash);
        Assert.Contains("英雄", File.ReadAllText(path));   // 中文不转义，直接用记事本也看得懂
    }

    [Fact]
    public void InfoJsonOfSeveralExportsSharesOneSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        var first = new CatalogInfoEntity("英雄", "Ahri", "Base", "assets/characters/ahri/base/",
            Path.Combine("Champions", "ahri", "base"), 2, 300, Array.Empty<CatalogInfoEntry>());
        var second = new CatalogInfoEntity("皮肤", "Spirit Blossom", "lillia_skin01", "assets/characters/lillia/",
            Path.Combine("Skins", "Spirit_Blossom", "lillia_skin01"), 3, 700, Array.Empty<CatalogInfoEntry>());

        CatalogInfo.Write(root, "LolDataExtractor 1.1.1", new[] { first });
        var merged = CatalogInfo.Write(root, "LolDataExtractor 1.1.1", new[] { second });

        Assert.Equal(2, merged.EntityCount);
        Assert.Equal(5, merged.FileCount);
        Assert.Equal(1000, merged.TotalBytes);
        Assert.Equal(new[] { first.Folder, second.Folder }.OrderBy(f => f, StringComparer.OrdinalIgnoreCase),
            merged.Entities.Select(entity => entity.Folder));

        // 再导一次同一个条目只会换掉它自己那一项，不会多出一条。
        var again = CatalogInfo.Write(root, "LolDataExtractor 1.1.1", Array.Empty<CatalogInfoEntity>());
        Assert.Equal(2, again.EntityCount);
    }

    // ── classifier ───────────────────────────────────────────────────────────────────────────

    private static (CatalogGroup Group, CatalogEntity Entity) Classify(CatalogClassifier classifier, string path)
    {
        Assert.True(classifier.TryAssign(path, out var placement));
        return (classifier.Nodes.Groups[placement.Group], classifier.Nodes.Entities[placement.Entity]);
    }

    [Fact]
    public void ClassifiesTheShapesTheGameActuallyUses()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides(), new CatalogNodes());

        var model = Classify(classifier, "assets/characters/ahri/skins/skin05/particles/ahri_base_tx_cm.tex");
        Assert.Equal(CatalogSection.Champion, model.Group.Section);
        Assert.Equal("Ahri", model.Group.Label);
        Assert.Equal("Challenger Ahri", model.Entity.Label);

        var baseSkin = Classify(classifier, "assets/characters/ahri/skins/base/particles/x.tex");
        Assert.Equal("Base", baseSkin.Entity.Label);

        var voice = Classify(classifier, "assets/sounds/wwise2016/vo/en_us/characters/ahri/skins/base/ahri_base_vo_audio.bnk");
        Assert.Equal(CatalogSection.Champion, voice.Group.Section);
        Assert.Equal("Ahri", voice.Group.Label);
        Assert.Equal("Voice", voice.Entity.Label);

        var data = Classify(classifier, "data/characters/ahri/ahri.bin");
        Assert.Equal("Data", data.Entity.Label);

        var clientData = Classify(classifier, "plugins/rcp-be-lol-game-data/global/default/v1/champions/103.json");
        Assert.Equal("Data", clientData.Entity.Label);

        var hud = Classify(classifier, "assets/characters/ahri/hud/icons2d/ahri_q.tex");
        Assert.Equal("Icon", hud.Entity.Label);
    }

    [Fact]
    public void AGuessedFolderStaysOutOfTheCatalog()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides(), new CatalogNodes());

        // "zedshadow" is only a guess (Zed's shadow), and guesses are not shown at all.
        Assert.False(classifier.TryAssign("assets/characters/zedshadow/skins/base/x.dds", out _));
        Assert.False(classifier.TryAssign("assets/sounds/wwise2016/vo/en_us/characters/zedshadow/skins/base/x.bnk", out _));

        // A folder the tables do know still resolves.
        Assert.True(classifier.TryAssign("assets/characters/ahri/skins/base/x.dds", out var known));
        Assert.Equal("Ahri", classifier.Nodes.Groups[known.Group].Label);
    }

    [Fact]
    public void PathsNoSectionOwnsStayOutOfTheCatalog()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides(), new CatalogNodes());

        Assert.False(classifier.TryAssign("assets/maps/sr/map11/hibiki/floor.mapgeo", out _));
        Assert.False(classifier.TryAssign("assets/characters/inhibitor/x.tex", out _));   // not a champion
        Assert.False(classifier.TryAssign("plugins/rcp-fe-lol-static-assets/global/default/images/x.png", out _));
        Assert.False(classifier.TryAssign("some/path/unknown.bin", out _));
    }

    [Fact]
    public void ItemAndPerkPathsUseTheTables()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides(), new CatalogNodes());

        var icon = Classify(classifier, "assets/items/icons2d/1001_class_t1_bootsofspeed.dds");
        Assert.Equal(CatalogSection.Item, icon.Group.Section);
        Assert.Equal("Boots", icon.Entity.Label);

        var script = Classify(classifier, "data/items/1004.luabin64");
        Assert.Equal("Faerie Charm", script.Entity.Label);

        var rune = Classify(classifier, "assets/perks/styles/domination/darkharvest/darkharvest.tex");
        Assert.Equal(CatalogSection.Perk, rune.Group.Section);
        Assert.Equal("Domination", rune.Group.Label);
        Assert.Equal("Dark Harvest", rune.Entity.Label);
    }

    [Fact]
    public void AnOverrideRuleMovesTheNode()
    {
        var overrides = Overrides();
        overrides.AddRule(new CatalogRule
        {
            PathPrefix = "assets/characters/ahri/",
            Group = "测试组",
            Note = "测试用",
        });

        var nodes = new CatalogNodes();
        var classifier = new CatalogClassifier(Tables(), overrides, nodes);
        const string path = "assets/characters/ahri/skins/base/x.dds";
        Assert.True(classifier.TryAssign(path, out var placement));

        Assert.True(classifier.Apply(PathHashing.Compute(path), path, ref placement));
        Assert.Equal("测试组", nodes.Groups[placement.Group].Label);   // the pinned group wins over the automatic one
        Assert.Equal(CatalogSection.Champion, nodes.Groups[placement.Group].Section);
    }

    [Fact]
    public void AnOverrideCanMoveTheSection()
    {
        var overrides = Overrides();
        overrides.AddRule(new CatalogRule
        {
            PathPrefix = "assets/characters/ahri/",
            Section = "装备",
        });

        var nodes = new CatalogNodes();
        var classifier = new CatalogClassifier(Tables(), overrides, nodes);
        const string path = "assets/characters/ahri/skins/base/x.dds";
        Assert.True(classifier.TryAssign(path, out var placement));
        Assert.True(classifier.Apply(PathHashing.Compute(path), path, ref placement));

        Assert.Equal(CatalogSection.Item, nodes.Groups[placement.Group].Section);
        Assert.Equal("Ahri", nodes.Groups[placement.Group].Label);
    }

    [Fact]
    public void AHiddenEntryDisappearsFromTheCatalog()
    {
        const string path = "assets/characters/ahri/skins/base/x.dds";
        var overrides = Overrides();
        overrides.SetEntry(PathHashing.Compute(path), new CatalogEntryRule { Hidden = true });

        var classifier = new CatalogClassifier(Tables(), overrides, new CatalogNodes());
        Assert.True(classifier.TryAssign(path, out var placement));
        Assert.False(classifier.Apply(PathHashing.Compute(path), path, ref placement));
    }

    [Fact]
    public void OverridesSurviveAReload()
    {
        var file = TempFile();
        try
        {
            var overrides = CatalogOverrides.Load(file);
            overrides.AddRule(new CatalogRule { PathPrefix = "assets/characters/kiwi_", Section = "表情", Group = "Champions", Entity = "Kiwi" });
            overrides.SetEntry(0x1234UL, new CatalogEntryRule { Section = "装备" });

            var reloaded = CatalogOverrides.Load(file);
            var rule = reloaded.RuleFor("assets/characters/kiwi_pet/x.dds");
            Assert.NotNull(rule);
            Assert.Equal("表情", rule!.Section);
            Assert.Equal("Champions", rule.Group);
            Assert.Equal("Kiwi", rule.Entity);
            Assert.Equal("装备", reloaded.EntryOf(0x1234UL)?.Section);
            Assert.Equal(rule.PathPrefix, reloaded.RuleFor("assets/characters/kiwi_other/x.dds")?.PathPrefix);
            Assert.Null(reloaded.RuleFor("assets/characters/ahri/x.dds"));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void BuildsAGroupedCatalogAndSearchesIt()
    {
        var wadPath = TestData.Wad("Ahri.wad.client");
        Assert.NotNull(wadPath);

        // The sample wad only carries hashes, so the rows are named with paths invented here; the
        // catalog resolves every hash through the table, which is exactly what a real scan does.
        var paths = new[]
        {
            "assets/characters/ahri/skins/base/particles/ahri_base_trail_01.tex",
            "assets/characters/ahri/skins/base/particles/ahri_base_smoke.tex",
            "assets/sounds/wwise2016/vo/en_us/characters/ahri/skins/base/ahri_base_vo.bnk",
            "plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png",
            "assets/maps/sr/map11/hibiki/floor.mapgeo",
            "some/path/unknown.bin",
        };

        List<ulong> hashes;
        using (var archive = WadArchive.Open(wadPath!))
            hashes = archive.ReadEntries(null).Select(entry => entry.Hash).Distinct().Take(paths.Length).ToList();
        Assert.Equal(paths.Length, hashes.Count);

        // Paths the wad does not carry still sit in the name table, so a row's name index is never
        // its row number; the anchor has to be decoded through that index.
        var padded = hashes.Select((hash, index) => (Hash: hash, Path: paths[index]))
            .ToDictionary(pair => pair.Hash, pair => pair.Path);
        for (var i = 0; i < 8; i++)
        {
            var path = $"assets/zzz/padding{i:00}.txt";
            padded[PathHashing.Compute(path)] = path;
        }

        var names = NameTable.FromMap(padded);
        var summary = new WadSummary(wadPath!, "Ahri.wad.client", "Ahri.wad.client",
            new FileInfo(wadPath!).Length, hashes.Count, 0, 0, new Dictionary<string, int>());

        var catalog = AssetCatalog.Build(new[] { summary }, names, Overrides(), Tables());

        // Only the paths a section owns become rows; the two others are left out.
        Assert.Equal(4, catalog.Count);
        Assert.Equal(4, catalog.CountOf(CatalogSection.Champion));
        Assert.Equal(0, catalog.CountOf(CatalogSection.Skin));

        var hits = catalog.Search("ahri_base_trail", 10);
        Assert.Equal(1, hits.Total);
        Assert.Equal(paths[0], hits.Entries[0].Path);
        Assert.Equal(CatalogSection.Champion, hits.Entries[0].Section);
        Assert.Equal("Ahri", catalog.Groups[hits.Entries[0].Group].Label);

        // The blob scan is case insensitive, and a hit is reported once per path.
        Assert.Equal(1, catalog.Search("AHRI_BASE_TRAIL", 10).Total);
        Assert.Equal(3, catalog.Search("ahri_base", 10).Total);   // both textures and the voice bank

        // The entity anchor is the folder every file of the entity shares, so exports stay short.
        var entry = catalog.EntriesOf(hits.Entries[0].Group, hits.Entries[0].Entity)
            .First(item => item.Hash == hits.Entries[0].Hash);
        Assert.Equal("ahri_base_trail_01.tex",
            CatalogPaths.RelativeOf(entry.Path, catalog.Entities[entry.Entity].Anchor));
        Assert.Equal("assets/characters/ahri/skins/base/particles/", catalog.Entities[entry.Entity].Anchor);
    }

    [Fact]
    public void SectionListIsStable()
    {
        Assert.Equal(CatalogSection.Champion, CatalogSectionInfo.All[0].Section);
        Assert.All(CatalogSectionInfo.All, info => Assert.False(string.IsNullOrWhiteSpace(info.Folder)));
        Assert.Equal(CatalogSectionInfo.All.Count, CatalogSectionInfo.All.Select(info => info.Display).Distinct().Count());
    }
}
