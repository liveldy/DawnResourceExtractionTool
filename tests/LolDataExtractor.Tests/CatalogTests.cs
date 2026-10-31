using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>
/// The catalog is the only place where folder names are guessed at, so the rules that decide
/// "this belongs to Ahri" are pinned here. The shapes come from the real client tables.
/// </summary>
public class CatalogTests
{
    /// <summary>
    /// The localized copy, in the shape the client writes it: <c>name</c> is the title and
    /// <c>description</c> the champion. Neither is used for the label — the alias is.
    /// </summary>
    private const string SummaryJson = """
        [
          { "id": -1, "name": "None", "description": "", "alias": "None" },
          { "id": 103, "name": "九尾妖狐", "description": "阿狸", "alias": "Ahri" },
          { "id": 1, "name": "黑暗之女", "description": "安妮", "alias": "Annie" },
          { "id": 238, "name": "影流之镰", "description": "劫", "alias": "Zed" }
        ]
        """;

    /// <summary>A mode variant of a champion: same display name, different alias.</summary>
    private const string VariantSummaryJson = """
        [
          { "id": 103, "name": "Ahri", "description": "the Nine-Tailed Fox", "alias": "Ahri" },
          { "id": 60103, "name": "Ahri", "description": "The Nine-Tailed Fox", "alias": "Jade_Ahri" }
        ]
        """;

    private const string SkinsJson = """
        {
          "103000": {
            "id": 103000, "isBase": true, "name": "Ahri", "isLegacy": false,
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Base/Images/ahri_splash_centered_0.jpg"
          },
          "103005": {
            "id": 103005, "isBase": false, "name": "Challenger Ahri", "isLegacy": false,
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/ahri_splash_centered_5.jpg"
          },
          "1001": {
            "id": 1001, "isBase": false, "name": "Goth Annie", "isLegacy": false,
            "splashPath": "/lol-game-data/assets/ASSETS/Characters/Annie/Skins/Skin01/Images/annie_splash_centered_1.jpg"
          }
        }
        """;

    private static CatalogTables Tables() => CatalogTables.FromJson(SummaryJson, SkinsJson);

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
        Assert.Equal(3, tables.Skins.Count);          // skins.json is an object keyed by id
        Assert.True(tables.TryGetChampionByAlias("ahri", out var ahri));
        Assert.Equal(103, ahri.Id);
        Assert.Equal("Ahri", ahri.Label);             // the alias, never the localized name

        Assert.True(tables.TryGetSkin(103005, out var skin));
        Assert.Equal("Challenger Ahri", skin.Name);
        Assert.Equal(103, skin.ChampionId);
    }

    [Fact]
    public void LabelIsTheAliasEvenWhenTheDisplayNameRepeats()
    {
        var tables = CatalogTables.FromJson(VariantSummaryJson, SkinsJson);

        Assert.True(tables.TryGetChampionByAlias("Jade_Ahri", out var variant));
        Assert.Equal(60103, variant.Id);
        Assert.Equal("Jade_Ahri", variant.Label);   // "Ahri" twice would make the two nodes look alike
    }

    [Fact]
    public void BrokenTablesOnlyCostLabels()
    {
        var tables = CatalogTables.FromJson("{ not json", null);

        Assert.True(tables.IsEmpty);
        Assert.False(tables.TryGuessChampion("ahri", out _, out _));
    }

    [Fact]
    public void ResolvesSkinFromItsImageFolder()
    {
        var tables = Tables();

        Assert.True(tables.TryGetSkinByFolder("ahri", "skin05", out var skin));
        Assert.Equal(103005, skin.Id);
        Assert.True(tables.TryGetSkinByFolder("Annie", "Skin01", out var annieSkin));
        Assert.Equal(1001, annieSkin.Id);
        Assert.False(tables.TryGetSkinByFolder("ahri", "skin42", out _));
    }

    [Fact]
    public void ExtractsTheFolderPairOfAPath()
    {
        Assert.Equal("ahri|skin05",
            CatalogTables.SkinFolderOf("/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/x.jpg"));
        Assert.Equal("ahri", CatalogTables.ChampionFolderOf("assets/characters/ahri/skins/base/particles/x.tex"));
        Assert.Equal("base", CatalogTables.SkinFolderIn("assets/characters/ahri/skins/base/particles/x.tex"));
        Assert.Null(CatalogTables.SkinFolderOf("/lol-game-data/assets/v1/champion-icons/103.png"));
    }

    [Theory]
    [InlineData("ahri", 103, false)]                 // exact alias
    [InlineData("Ahri", 103, false)]
    [InlineData("zedshadow", 238, true)]             // starts with an alias
    [InlineData("annietibbers", 1, true)]
    [InlineData("jade_ahri", 103, true)]             // mode prefix dropped
    [InlineData("cherry_goh_zed", 238, true)]        // ends with an alias
    public void GuessesChampionsFromFolderNames(string folder, int championId, bool guessed)
    {
        Assert.True(Tables().TryGuessChampion(folder, out var champion, out var isGuess));
        Assert.Equal(championId, champion.Id);
        Assert.Equal(guessed, isGuess);
    }

    [Theory]
    [InlineData("sightward")]        // not a champion at all
    [InlineData("tft17_jinx")]       // a mode prefix gains nothing when the table has no Jinx
    [InlineData("")]
    [InlineData(null)]
    public void LeavesUnknownFoldersAlone(string? folder)
        => Assert.False(Tables().TryGuessChampion(folder, out _, out _));

    // ── path helpers ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/lol-game-data/assets/v1/champion-icons/103.png")]
    [InlineData("/lol-game-data/assets/ASSETS/Characters/Ahri/Skins/Skin05/Images/x.jpg")]
    public void ClientCandidatesCoverBothLocales(string reference)
    {
        var candidates = CatalogPaths.ClientCandidates(reference);

        Assert.Equal(4, candidates.Count);
        Assert.Contains(candidates, path => path.StartsWith("plugins/rcp-be-lol-game-data/global/default/", StringComparison.Ordinal));
        Assert.Contains(candidates, path => path.StartsWith("plugins/rcp-be-lol-game-data/global/zh_cn/", StringComparison.Ordinal));
    }

    [Fact]
    public void ClientCandidatesTryThePathWithAndWithoutTheAssetsPrefix()
    {
        var candidates = CatalogPaths.ClientCandidates("/lol-game-data/assets/v1/champion-icons/103.png");

        Assert.Contains("plugins/rcp-be-lol-game-data/global/default/assets/v1/champion-icons/103.png", candidates);
        Assert.Contains("plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png", candidates);
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
    public void RelativePathDropsTheChampionAndSkinAnchor()
    {
        Assert.Equal(Path.Combine("particles", "ahri_base_tx_cm.tex"),
            CatalogPaths.RelativeOf("assets/characters/ahri/skins/base/particles/ahri_base_tx_cm.tex", "ahri", "base"));
        Assert.Equal(Path.Combine("v1", "champion-icons", "103.png"),
            CatalogPaths.RelativeOf("plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png", string.Empty, string.Empty));
    }

    [Fact]
    public void SanitizesNamesWindowsRefuses()
    {
        Assert.Equal("a_b", CatalogPaths.SanitizePath("a:b").Replace(Path.DirectorySeparatorChar, '_'));
        Assert.Equal("unnamed", CatalogPaths.SafeFolderOf("  "));
        Assert.Equal("阿狸_Ahri", CatalogPaths.SafeFolderOf("阿狸 Ahri"));
    }

    // ── classifier ───────────────────────────────────────────────────────────────────────────

    private static (CatalogCategory Category, CatalogHero Hero, CatalogSkinLabel Skin) Classify(
        CatalogClassifier classifier, string path)
    {
        Assert.True(classifier.Classify(path, PathHashing.Compute(path), out var category, out var hero, out var skin));
        return (category, classifier.Heroes[hero], classifier.Skins[skin]);
    }

    [Fact]
    public void ClassifiesTheShapesTheGameActuallyUses()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides());

        var splash = Classify(classifier, "assets/characters/ahri/skins/base/images/ahri_splash_centered_0.jpg");
        Assert.Equal(CatalogCategory.HeroImage, splash.Category);
        Assert.Equal("Ahri", splash.Hero.Label);
        Assert.Equal(CatalogHeroKind.Champion, splash.Hero.Kind);
        Assert.Equal("基础皮肤", splash.Skin.Label);
        Assert.Equal(103000, splash.Skin.SkinId);

        var model = Classify(classifier, "assets/characters/ahri/skins/skin05/particles/ahri_base_tx_cm.tex");
        Assert.Equal(CatalogCategory.HeroAsset, model.Category);
        Assert.Equal("皮肤 05 Challenger Ahri", model.Skin.Label);
        Assert.True(model.Skin.IsKnown);

        var voice = Classify(classifier, "assets/sounds/wwise2016/vo/en_us/characters/ahri/skins/base/ahri_base_vo_audio.bnk");
        Assert.Equal(CatalogCategory.HeroAudio, voice.Category);
        Assert.Equal("Ahri", voice.Hero.Label);

        var icon = Classify(classifier, "plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png");
        Assert.Equal(CatalogCategory.HeroImage, icon.Category);
        Assert.Equal("Ahri", icon.Hero.Label);
    }

    [Fact]
    public void AGuessedFolderKeepsItsOwnNode()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides());

        var result = Classify(classifier, "assets/characters/zedshadow/skins/base/x.dds");

        Assert.Equal(CatalogHeroKind.Guessed, result.Hero.Kind);
        Assert.Equal(238, result.Hero.ChampionId);
        Assert.Equal("zedshadow", result.Hero.Folder);
        Assert.Equal("Zed · zedshadow", result.Hero.Label);
    }

    [Fact]
    public void BucketsWithoutAChampionGroupByFolder()
    {
        var classifier = new CatalogClassifier(Tables(), Overrides());

        var map = Classify(classifier, "assets/maps/sr/map11/hibiki/foo.mapgeo");
        Assert.Equal(CatalogCategory.Map, map.Category);
        Assert.Equal("sr", map.Hero.Label);
        Assert.Equal("map11", map.Skin.Label);

        var loose = Classify(classifier, "some/path/unknown.bin");
        Assert.Equal(CatalogCategory.Unclassified, loose.Category);
        Assert.Equal("some", loose.Hero.Label);
    }

    [Fact]
    public void AnOverrideRuleBeatsTheAutomaticResult()
    {
        var overrides = Overrides();
        overrides.AddRule(new CatalogRule
        {
            PathPrefix = "assets/characters/zedshadow/",
            Category = "英雄资源",
            Champion = "ahri",
            Note = "测试用",
        });

        var result = Classify(new CatalogClassifier(Tables(), overrides), "assets/characters/zedshadow/skins/base/x.dds");

        Assert.Equal(CatalogCategory.HeroAsset, result.Category);   // dds would have been an image
        Assert.Equal("Ahri", result.Hero.Label);                     // the pinned champion wins over the guess
    }

    [Fact]
    public void AHiddenEntryDisappearsFromTheCatalog()
    {
        const string path = "assets/characters/ahri/skins/base/x.dds";
        var overrides = Overrides();
        overrides.SetEntry(PathHashing.Compute(path), new CatalogEntryRule { Hidden = true });

        Assert.False(new CatalogClassifier(Tables(), overrides)
            .Classify(path, PathHashing.Compute(path), out _, out _, out _));
    }

    [Fact]
    public void OverridesSurviveAReload()
    {
        var file = TempFile();
        try
        {
            var overrides = CatalogOverrides.Load(file);
            overrides.AddRule(new CatalogRule { PathPrefix = "assets/characters/kiwi_", Category = "共享资源", Champion = "Zed" });
            overrides.SetEntry(0x1234UL, new CatalogEntryRule { Category = "物品与装备" });

            var reloaded = CatalogOverrides.Load(file);
            var rule = reloaded.RuleFor("assets/characters/kiwi_pet/x.dds");
            Assert.NotNull(rule);
            Assert.Equal("共享资源", rule!.Category);
            Assert.Equal("Zed", rule.Champion);
            Assert.Equal("物品与装备", reloaded.EntryOf(0x1234UL)?.Category);
            Assert.Equal(rule.PathPrefix, reloaded.RuleFor("assets/characters/kiwi_other/x.dds")?.PathPrefix);
            Assert.Null(reloaded.RuleFor("assets/characters/ahri/x.dds"));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Theory]
    [InlineData("HeroImage")]
    [InlineData("heroimage")]
    [InlineData("英雄图像")]
    public void CategoryKeysAcceptTheEnumOrTheLabel(string key)
    {
        Assert.True(CatalogCategoryInfo.TryParse(key, out var category));
        Assert.Equal(CatalogCategory.HeroImage, category);
    }

    [Fact]
    public void ExportPathKeepsCategoryHeroAndSkin()
    {
        var ahri = new CatalogHero("Ahri", "ahri", 103, CatalogHeroKind.Champion);
        var skin05 = new CatalogSkinLabel("皮肤 05 Challenger Ahri", "skin05", 103005, 103, true);
        var entry = new CatalogEntry(1,
            "assets/characters/ahri/skins/skin05/particles/ahri_base_trail_01.tex",
            @"D:\x\Ahri.wad.client", CatalogCategory.HeroAsset, 1, 1, 100, 1);

        Assert.Equal(Path.Combine("英雄资源", "ahri", "skin05", "particles", "ahri_base_trail_01.tex"),
            CatalogPaths.ExportPathOf(entry, ahri, skin05));

        // No skin: the file keeps the rest of its own folders under the hero.
        var loose = entry with { Path = "assets/characters/ahri/hud/ahri_circle_0.tex", Category = CatalogCategory.HeroImage };
        Assert.Equal(Path.Combine("英雄图像", "ahri", "hud", "ahri_circle_0.tex"),
            CatalogPaths.ExportPathOf(loose, ahri, null));

        // A group the tables could not name still exports under its folder, not under "unnamed".
        var folder = new CatalogHero("zedshadow", "zedshadow", 0, CatalogHeroKind.Folder);
        Assert.Equal(Path.Combine("英雄资源", "zedshadow", "particles", "x.tex"),
            CatalogPaths.ExportPathOf(entry with { Path = "assets/characters/zedshadow/particles/x.tex" }, folder, null));

        Assert.Equal(Path.Combine("英雄资源", "未指派", "particles", "x.tex"),
            CatalogPaths.ExportPathOf(entry with { Path = "particles/x.tex" }, null, null));
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
            "assets/maps/sr/map11/hibiki/floor.mapgeo",
            "plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/103.png",
            "some/path/unknown.bin",
        };

        List<ulong> hashes;
        using (var archive = WadArchive.Open(wadPath!))
            hashes = archive.ReadEntries(null).Select(entry => entry.Hash).Distinct().Take(paths.Length).ToList();
        Assert.Equal(paths.Length, hashes.Count);

        var names = NameTable.FromMap(hashes.Select((hash, index) => (Hash: hash, Path: paths[index]))
            .ToDictionary(pair => pair.Hash, pair => pair.Path));
        var summary = new WadSummary(wadPath!, "Ahri.wad.client", "Ahri.wad.client",
            new FileInfo(wadPath!).Length, hashes.Count, 0, 0, new Dictionary<string, int>());

        var catalog = AssetCatalog.Build(new[] { summary }, names, Overrides(), Tables());

        // Every entry of the wad is a row; the named ones are classified, the rest stay unclassified.
        Assert.True(catalog.Count > paths.Length);
        Assert.Equal(catalog.Count - paths.Length + 1, catalog.CountOf(CatalogCategory.Unclassified));
        Assert.Equal(2, catalog.CountOf(CatalogCategory.HeroAsset));
        Assert.Equal(1, catalog.CountOf(CatalogCategory.HeroAudio));
        Assert.Equal(1, catalog.CountOf(CatalogCategory.HeroImage));
        Assert.Equal(1, catalog.CountOf(CatalogCategory.Map));

        var hits = catalog.Search("ahri_base_trail", 10);
        Assert.Equal(1, hits.Total);
        Assert.Equal(paths[0], hits.Entries[0].Path);
        Assert.Equal(CatalogCategory.HeroAsset, hits.Entries[0].Category);

        // The blob scan is case insensitive, and a hit is reported once per path.
        Assert.Equal(1, catalog.Search("AHRI_BASE_TRAIL", 10).Total);
        Assert.Equal(3, catalog.Search("ahri_base", 10).Total);   // both textures and the voice bank
    }

    [Fact]
    public void UnclassifiedIsTheLastCategory()
    {
        Assert.Equal(CatalogCategory.Unclassified, CatalogCategoryInfo.All[^1].Category);
        Assert.All(CatalogCategoryInfo.All, info => Assert.False(string.IsNullOrWhiteSpace(info.Folder)));
    }
}
