namespace LolDataExtractor.Core;

/// <summary>
/// Turns an asset path into a bucket, a hero and a skin. The client tables decide who is who;
/// folder names are only trusted as a guess, and the user's overrides always win.
/// </summary>
internal sealed class CatalogClassifier
{
    private readonly CatalogTables _tables;
    private readonly CatalogOverrides _overrides;
    private readonly IReadOnlyList<CatalogRule> _rules;
    private readonly List<CatalogHero> _heroes = new();
    private readonly List<CatalogSkinLabel> _skins = new();
    private readonly Dictionary<int, ushort> _heroByChampion = new();
    private readonly Dictionary<string, ushort> _heroByFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ushort> _skinByKey = new(StringComparer.OrdinalIgnoreCase);

    public CatalogClassifier(CatalogTables tables, CatalogOverrides overrides)
    {
        _tables = tables;
        _overrides = overrides;
        _rules = overrides.RulesBySpecificity();

        // Index 0 is "no hero": unmatched entries still need a slot.
        _heroes.Add(new CatalogHero("未指派", string.Empty, 0, CatalogHeroKind.Folder));
        _skins.Add(new CatalogSkinLabel("未指派", string.Empty, 0, 0, false));

        foreach (var champion in tables.Champions) RegisterChampion(champion);
    }

    public CatalogTables Tables => _tables;

    public IReadOnlyList<CatalogHero> Heroes => _heroes;

    public IReadOnlyList<CatalogSkinLabel> Skins => _skins;

    /// <summary>
    /// Classifies one entry. Returns false when an override hides it; the out values are only
    /// meaningful when it returns true.
    /// </summary>
    public bool Classify(string path, ulong hash, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        ClassifyAuto(path, out category, out hero, out skin);

        if (_overrides.EntryOf(hash) is { } entry)
        {
            if (entry.Hidden) return false;
            if (CatalogCategoryInfo.TryParse(entry.Category, out var pinned)) category = pinned;
            hero = HeroFromLabel(entry.Champion, hero);
            skin = SkinFromLabel(hero, entry.Skin, skin);
        }

        foreach (var rule in _rules)
        {
            if (!path.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.Hidden) return false;
            if (CatalogCategoryInfo.TryParse(rule.Category, out var pinned)) category = pinned;
            hero = HeroFromLabel(rule.Champion, hero);
            skin = SkinFromLabel(hero, rule.Skin, skin);
            break;
        }

        return true;
    }

    // ── hero and skin bookkeeping ────────────────────────────────────────────────────────────

    private ushort RegisterChampion(CatalogChampion champion)
    {
        if (_heroByChampion.TryGetValue(champion.Id, out var existing)) return existing;
        var index = AddHero(new CatalogHero(champion.Label, champion.Alias.ToLowerInvariant(), champion.Id, CatalogHeroKind.Champion));
        _heroByChampion[champion.Id] = index;
        _heroByFolder[champion.Alias] = index;
        return index;
    }

    private ushort AddHero(CatalogHero hero)
    {
        if (_heroes.Count >= ushort.MaxValue) return 0;
        _heroes.Add(hero);
        return (ushort)(_heroes.Count - 1);
    }

    /// <summary>Hero of a directory below <c>characters/</c>, guessing when the name is not an alias.</summary>
    private ushort HeroForFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return 0;
        if (_heroByFolder.TryGetValue(folder, out var cached)) return cached;

        ushort index;
        if (_tables.TryGuessChampion(folder, out var champion, out var guessed))
        {
            // A guessed folder keeps its own node: "Zed shadow" stays next to Zed instead of being
            // mixed into his assets, and can be moved by hand.
            index = guessed
                ? AddHero(new CatalogHero($"{champion.Label} · {folder}", folder.ToLowerInvariant(), champion.Id, CatalogHeroKind.Guessed))
                : RegisterChampion(champion);
        }
        else
        {
            index = AddHero(new CatalogHero(folder, folder.ToLowerInvariant(), 0, CatalogHeroKind.Folder));
        }

        _heroByFolder[folder] = index;
        return index;
    }

    private ushort HeroForChampionId(int id)
    {
        if (id <= 0) return 0;
        if (_heroByChampion.TryGetValue(id, out var index)) return index;
        if (!_tables.TryGetChampionById(id, out var champion)) return 0;
        return RegisterChampion(champion);
    }

    /// <summary>Override text: a champion alias, a free label, or empty to keep the automatic hero.</summary>
    private ushort HeroFromLabel(string? label, ushort fallback)
    {
        if (string.IsNullOrWhiteSpace(label)) return fallback;
        var text = label.Trim();
        if (_tables.TryGetChampionByAlias(text, out var champion)) return RegisterChampion(champion);

        if (_heroByFolder.TryGetValue(text, out var existing)) return existing;
        return AddHero(new CatalogHero(text, CatalogPaths.SafeFolderOf(text), 0, CatalogHeroKind.Folder));
    }

    private ushort SkinFromLabel(ushort hero, string? label, ushort fallback)
    {
        if (string.IsNullOrWhiteSpace(label)) return fallback;
        return SkinFor(hero, _heroes[hero].Folder, label.Trim());
    }

    /// <summary>Skin of one champion folder; the tables decide the name, the rest is the folder.</summary>
    private ushort SkinFor(ushort hero, string championFolder, string skinFolder)
    {
        if (string.IsNullOrWhiteSpace(skinFolder)) return 0;

        var key = hero + "|" + skinFolder;
        if (_skinByKey.TryGetValue(key, out var cached)) return cached;

        var championId = _heroes[hero].ChampionId;
        CatalogSkin? skin = null;
        if (!_tables.TryGetSkinByFolder(championFolder, skinFolder, out skin!))
        {
            if (championId > 0 && TryNumericSkinId(championId, skinFolder, out var id))
                _tables.TryGetSkin(id, out skin!);
        }

        var folder = skinFolder.ToLowerInvariant();
        var label = LabelOf(folder, skin);
        var index = AddSkin(new CatalogSkinLabel(label, folder, skin?.Id ?? 0, championId, skin is not null));
        _skinByKey[key] = index;
        return index;
    }

    private ushort AddSkin(CatalogSkinLabel skin)
    {
        if (_skins.Count >= ushort.MaxValue) return 0;
        _skins.Add(skin);
        return (ushort)(_skins.Count - 1);
    }

    private static string LabelOf(string folder, CatalogSkin? skin)
    {
        if (string.Equals(folder, "base", StringComparison.OrdinalIgnoreCase)) return "基础皮肤";
        if (skin is null) return folder;

        var number = skin.Id % 1000;
        return number == 0 ? "基础皮肤" : $"皮肤 {number:D2} {skin.Name}";
    }

    /// <summary>Skin number of a folder name inside one champion, validated against the tables.</summary>
    private static bool TryNumericSkinId(int championId, string folder, out int id)
    {
        id = 0;
        if (string.Equals(folder, "base", StringComparison.OrdinalIgnoreCase))
        {
            id = championId * 1000;
            return true;
        }

        if (!folder.StartsWith("skin", StringComparison.OrdinalIgnoreCase)) return false;
        var digits = folder[4..];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return false;
        if (!int.TryParse(digits, out var number) || number <= 0 || number > 999) return false;

        id = championId * 1000 + number;
        return true;
    }

    /// <summary>Skin folder name for a numeric skin id, so client images land in the same group.</summary>
    private static string SkinFolderOfId(int skinId)
    {
        var number = skinId % 1000;
        return number == 0 ? "base" : $"skin{number:D2}";
    }

    // ── path rules ──────────────────────────────────────────────────────────────────────────

    private void ClassifyAuto(string path, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        category = CatalogCategory.Unclassified;
        hero = 0;
        skin = 0;
        if (path.Length == 0) return;

        if (path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)) ClassifyClient(path, out category, out hero, out skin);
        else ClassifyGame(path, out category, out hero, out skin);

        // A bucket without a champion still needs a second level: group it by the folder it lives in
        // (ux, loot, materials, sr, …) instead of dumping tens of thousands of files into one node.
        if (hero == 0)
        {
            var (kind, sub) = GroupFoldersOf(path);
            if (kind.Length > 0) hero = HeroForFolder(kind);
            if (sub.Length > 0 && skin == 0) skin = SkinFor(hero, _heroes[hero].Folder, sub);
        }
    }

    /// <summary>
    /// Two folder levels that name the group of an entry: the kind folder one or two levels below
    /// <c>assets/</c>, <c>data/</c> or <c>v1/</c> (the sound bank for sound assets), then the first
    /// folder after it. Numeric folders such as item or map ids become the sub level, so a bucket
    /// never ends up as thousands of nodes named after an id.
    /// </summary>
    private static (string Kind, string Sub) GroupFoldersOf(string path)
    {
        var rest = CatalogPaths.StripPluginPrefix(path);
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return (string.Empty, string.Empty);

        var first = segments[0];
        var start = 0;
        var rootToken = string.Empty;
        if (first.Equals("assets", StringComparison.OrdinalIgnoreCase)
            || first.Equals("data", StringComparison.OrdinalIgnoreCase)
            || first.Equals("v1", StringComparison.OrdinalIgnoreCase)
            || first.Equals("characters", StringComparison.OrdinalIgnoreCase))
        {
            rootToken = segments.Length > 1 ? TrimUnitName(segments[1]) : string.Empty;
            start = 2;
            if (first.Equals("assets", StringComparison.OrdinalIgnoreCase)
                && segments.Length > 3
                && segments[1].Equals("sounds", StringComparison.OrdinalIgnoreCase))
            {
                // assets/sounds/wwise2016/<bank>/… : the bank is the kind.
                rootToken = "sounds";
                start = 3;
            }
        }

        // The last segment is the file itself; only folder segments name a group.
        var folders = segments.Length - 1;
        var kind = string.Empty;
        var sub = string.Empty;
        var pendingNumber = string.Empty;
        for (var i = start; i < folders; i++)
        {
            var segment = segments[i];
            if (IsNumeric(segment))
            {
                if (kind.Length > 0 && sub.Length == 0) sub = segment;
                else if (kind.Length == 0) pendingNumber = segment;
                continue;
            }

            if (kind.Length == 0) kind = segment;
            else if (sub.Length == 0) sub = segment;
        }

        if (kind.Length == 0)
        {
            kind = rootToken.Length > 0 ? rootToken : TrimUnitName(segments[^1]);
            if (pendingNumber.Length > 0) sub = pendingNumber;
        }

        return (kind, sub);
    }

    private static bool IsNumeric(string text)
    {
        if (text.Length == 0) return false;
        foreach (var c in text)
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>
    /// A single file must not become its own node: "petdowsie_multi_skins_….bin" is one companion,
    /// so the stem and the "multi" suffix are dropped from the group name.
    /// </summary>
    private static string TrimUnitName(string label)
    {
        var dot = label.LastIndexOf('.');
        if (dot > 0) label = label[..dot];

        var multi = label.IndexOf("_multi", StringComparison.OrdinalIgnoreCase);
        return multi > 0 ? label[..multi] : label;
    }

    private void ClassifyGame(string path, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        category = CatalogCategory.Unclassified;
        hero = 0;
        skin = 0;

        if (path.StartsWith("assets/characters/", StringComparison.OrdinalIgnoreCase))
        {
            ClassifyCharacterPath(path, out category, out hero, out skin);
            return;
        }

        if (path.StartsWith("assets/sounds/", StringComparison.OrdinalIgnoreCase))
        {
            ClassifySoundPath(path, out category, out hero, out skin);
            return;
        }

        if (path.StartsWith("data/characters/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("characters/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.HeroConfig;
            var unit = CatalogTables.ChampionFolderOf(path) ?? Segment(path, 1) ?? string.Empty;
            hero = HeroForFolder(TrimUnitName(unit));
            skin = SkinFor(hero, _heroes[hero].Folder, CatalogTables.SkinFolderIn(path) ?? string.Empty);
            return;
        }

        if (path.StartsWith("data/shared/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("assets/shared/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/particles/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/spells/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shadercache", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Shared;
            return;
        }

        if (path.StartsWith("assets/maps/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("levels/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/maps/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/levels/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Map;
            return;
        }

        if (path.StartsWith("assets/items/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/items/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Item;
            return;
        }

        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("loadouts/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ux/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("uiautoatlas/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("clientstates/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("patching/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("lcu/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("global/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("missions/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Interface;
            return;
        }

        // Root files of a game wad: gameplay.*.bin and friends, i.e. interface data.
        if (path.IndexOf('/') < 0) category = CatalogCategory.Interface;
    }

    private void ClassifyCharacterPath(string path, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        var folder = CatalogTables.ChampionFolderOf(path);
        hero = HeroForFolder(folder);
        skin = SkinFor(hero, folder ?? string.Empty, CatalogTables.SkinFolderIn(path) ?? string.Empty);
        category = IsImage(path) ? CatalogCategory.HeroImage : CatalogCategory.HeroAsset;
    }

    private void ClassifySoundPath(string path, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        category = CatalogCategory.HeroAudio;
        hero = 0;
        skin = 0;

        var folder = CatalogTables.ChampionFolderOf(path);
        if (folder is not null)
        {
            hero = HeroForFolder(folder);
            skin = SkinFor(hero, folder, CatalogTables.SkinFolderIn(path) ?? string.Empty);
            return;
        }

        // Music, ambience and companion sounds are not tied to one champion.
        category = CatalogCategory.Shared;
    }

    private void ClassifyClient(string path, out CatalogCategory category, out ushort hero, out ushort skin)
    {
        category = CatalogCategory.Interface;
        hero = 0;
        skin = 0;

        if (!path.Contains("rcp-be-lol-game-data", StringComparison.OrdinalIgnoreCase)) return;

        var rest = CatalogPaths.StripPluginPrefix(path);

        if (rest.StartsWith("assets/characters/", StringComparison.OrdinalIgnoreCase))
        {
            ClassifyCharacterPath(rest, out category, out hero, out skin);
            return;
        }

        if (rest.StartsWith("v1/champions/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.HeroConfig;
            hero = HeroForChampionId(LeadingNumber(FileNameOf(rest)));
            return;
        }

        if (rest.StartsWith("v1/champion-", StringComparison.OrdinalIgnoreCase))
        {
            // Images are the ones that carry a skin or champion number in the path; the tables
            // (champion-summary.json and friends) have none and stay under the interface bucket.
            ResolveChampionByNumber(rest, out hero, out skin);
            if (hero != 0) category = CatalogCategory.HeroImage;
            return;
        }

        if (rest.StartsWith("v1/items", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith("assets/items/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Item;
            return;
        }

        if (rest.StartsWith("assets/maps/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Map;
            return;
        }

        if (rest.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
        {
            category = CatalogCategory.Interface;
        }
    }

    /// <summary>
    /// Client image paths carry the skin id in the file name (<c>…/103/103001.jpg</c>) or the
    /// champion id alone (<c>…/champion-icons/103.png</c>).
    /// </summary>
    private void ResolveChampionByNumber(string rest, out ushort hero, out ushort skin)
    {
        hero = 0;
        skin = 0;

        var number = LeadingNumber(FileNameOf(rest));
        if (number >= 1000)
        {
            hero = HeroForChampionId(number / 1000);
            skin = SkinFor(hero, _heroes[hero].Folder, SkinFolderOfId(number));
            return;
        }

        if (number > 0)
        {
            hero = HeroForChampionId(number);
            return;
        }

        // Fall back to the first numeric folder segment.
        foreach (var segment in rest.Split('/'))
        {
            var value = LeadingNumber(segment);
            if (value <= 0) continue;
            if (value >= 1000)
            {
                hero = HeroForChampionId(value / 1000);
                skin = SkinFor(hero, _heroes[hero].Folder, SkinFolderOfId(value));
            }
            else
            {
                hero = HeroForChampionId(value);
            }

            return;
        }
    }

    private static bool IsImage(string path)
    {
        var dot = path.LastIndexOf('.');
        if (dot < 0) return false;
        var extension = path.AsSpan(dot + 1);
        return extension.Equals("jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("dds", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("tga", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("webp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("svg", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>Digits at the start of a file name: "103001.jpg" → 103001, else 0.</summary>
    private static int LeadingNumber(string name)
    {
        var digits = 0;
        while (digits < name.Length && char.IsAsciiDigit(name[digits])) digits++;
        if (digits == 0 || digits > 9) return 0;
        return int.TryParse(name.AsSpan(0, digits), out var value) ? value : 0;
    }

    private static string? Segment(string path, int index)
    {
        var segments = path.Split('/');
        return index < segments.Length ? segments[index] : null;
    }
}
