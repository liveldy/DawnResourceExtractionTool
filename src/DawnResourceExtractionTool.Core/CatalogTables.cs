using System.Text;
using System.Text.Json;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>One champion of the client's <c>champion-summary.json</c>.</summary>
public sealed record CatalogChampion(int Id, string Alias, string Name, string Description, string? PortraitPath)
{
    /// <summary>
    /// Label used in the tree: the English alias, which is also the folder name the game assets live
    /// in. The localized names are not used — "Jade_Ahri" and "Ahri" are different champions here,
    /// and their display names are both "Ahri" in either language.
    /// </summary>
    public string Label => Alias;
}

/// <summary>One chroma of a skin: its own images.</summary>
public sealed record CatalogChroma(int Id, string Name, IReadOnlyList<string> Paths);

/// <summary>One skin of the client's <c>skins.json</c>, with the images it points at.</summary>
public sealed record CatalogSkin(
    int Id,
    int ChampionId,
    string Name,
    bool IsBase,
    string? Description,
    int SkinLine,
    IReadOnlyList<string> Paths,
    IReadOnlyList<CatalogChroma> Chromas);

/// <summary>One skin line (系列) of <c>skinlines.json</c>.</summary>
public sealed record CatalogSkinLine(int Id, string Name, string? Description);

/// <summary>One universe (宇宙) of <c>universes.json</c>; it owns a list of skin lines.</summary>
public sealed record CatalogUniverse(int Id, string Name, string? Description, string? ImagePath, IReadOnlyList<int> SkinLines);

/// <summary>One skin border of <c>skinborders.json</c>.</summary>
public sealed record CatalogSkinBorder(string Name, string? Image);

/// <summary>One item of <c>items.json</c>.</summary>
public sealed record CatalogItem(int Id, string Name, string? Description, IReadOnlyList<string> Categories, string? IconPath);

/// <summary>One rune of <c>perks.json</c>.</summary>
public sealed record CatalogPerk(int Id, string Name, string? Description, string? IconPath);

/// <summary>One rune page style of <c>perkstyles.json</c>.</summary>
public sealed record CatalogPerkStyle(
    int Id,
    string Name,
    string? Description,
    string? IconPath,
    IReadOnlyList<string> Images,
    IReadOnlyList<int> Perks);

/// <summary>One banner of <c>regalia.json</c>.</summary>
public sealed record CatalogRegalia(string Id, string Name, string? Description, string Type, string? AssetPath);

/// <summary>One emote of <c>summoner-emotes.json</c>.</summary>
public sealed record CatalogEmote(int Id, string Name, string? Description, string? IconPath);

/// <summary>One summoner icon of <c>summoner-icons.json</c>.</summary>
public sealed record CatalogIcon(int Id, string Title, string? Description, string? ImagePath);

/// <summary>One icon set of <c>summoner-icon-sets.json</c>.</summary>
public sealed record CatalogIconSet(int Id, string Name, string? Description, IReadOnlyList<int> Icons);

/// <summary>One summoner spell of <c>summoner-spells.json</c>.</summary>
public sealed record CatalogSpell(int Id, string Name, string? Description, string? IconPath, IReadOnlyList<string> GameModes);

/// <summary>One ward skin of <c>ward-skins.json</c>.</summary>
public sealed record CatalogWard(int Id, string Name, string? Description, IReadOnlyList<string> Images);

/// <summary>One ward skin set of <c>ward-skin-sets.json</c>.</summary>
public sealed record CatalogWardSet(int Id, string Name, string? Description, IReadOnlyList<int> Wards);

/// <summary>
/// The client tables the catalog is organised around. They are the authority on who is who and on
/// which image belongs to which skin, item, rune, icon and so on; everything derived from a folder
/// name alone is a guess.
/// </summary>
public sealed class CatalogTables
{
    private const string CharactersMarker = "characters/";
    private const string SkinsMarker = "/skins/";
    private const int TablesRank = 20000;

    /// <summary>Client json file name → the section whose tree it documents.</summary>
    private static readonly Dictionary<string, CatalogSection> TableSections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["champion-summary.json"] = CatalogSection.Champion,
        ["skins.json"] = CatalogSection.Skin,
        ["skinlines.json"] = CatalogSection.Skin,
        ["universes.json"] = CatalogSection.Skin,
        ["skinborders.json"] = CatalogSection.Skin,
        ["items.json"] = CatalogSection.Item,
        ["perks.json"] = CatalogSection.Perk,
        ["perkstyles.json"] = CatalogSection.Perk,
        ["regalia.json"] = CatalogSection.Regalia,
        ["summoner-emotes.json"] = CatalogSection.Emote,
        ["summoner-icons.json"] = CatalogSection.Icon,
        ["summoner-icon-sets.json"] = CatalogSection.Icon,
        ["summoner-spells.json"] = CatalogSection.SummonerSpell,
        ["ward-skins.json"] = CatalogSection.Ward,
        ["ward-skin-sets.json"] = CatalogSection.Ward,
    };

    private readonly Dictionary<string, CatalogChampion> _byAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CatalogChampion> _byId = new();
    private readonly Dictionary<int, CatalogSkin> _skinsById = new();
    private readonly Dictionary<string, int> _skinByFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> _skinByChroma = new();
    private readonly Dictionary<int, CatalogSkinLine> _skinLines = new();
    private readonly Dictionary<int, CatalogUniverse> _universes = new();
    private readonly Dictionary<int, int> _universeOfLine = new();
    private readonly Dictionary<int, CatalogItem> _items = new();
    private readonly Dictionary<int, CatalogPerk> _perks = new();
    private readonly Dictionary<string, CatalogPerk> _perkByFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _perkStyleOf = new();
    private readonly Dictionary<string, CatalogPerkStyle> _perkStyleByFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CatalogEmote> _emotes = new();
    private readonly Dictionary<int, CatalogIcon> _icons = new();
    private readonly Dictionary<int, int> _iconSetOfIcon = new();
    private readonly Dictionary<int, CatalogIconSet> _iconSets = new();
    private readonly Dictionary<int, CatalogSpell> _spells = new();
    private readonly Dictionary<int, CatalogWard> _wards = new();
    private readonly Dictionary<int, int> _wardSetOfWard = new();
    private readonly Dictionary<int, CatalogWardSet> _wardSets = new();

    /// <summary>"ahri|skin05" → skin id, built from the image paths of every skin.</summary>
    private readonly Dictionary<string, int> _skinFolderIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Aliases longest first, so "leesin" wins over "lee" when guessing.</summary>
    private readonly string[] _aliasesByLength;

    /// <summary>Every (path, target) pair the tables imply; resolved into nodes on first build.</summary>
    private readonly List<(string Path, Func<CatalogNodes, CatalogPlacement> Target)> _references = new();

    /// <summary>The table json files themselves, as found in the client archives.</summary>
    private readonly List<(string Path, CatalogSection Section, string File)> _tableFiles = new();

    private CatalogTables(
        IReadOnlyList<CatalogChampion> champions,
        IReadOnlyList<CatalogSkin> skins,
        IReadOnlyList<CatalogSkinBorder> borders,
        IReadOnlyList<CatalogSkinLine> skinLines,
        IReadOnlyList<CatalogUniverse> universes,
        IReadOnlyList<CatalogItem> items,
        IReadOnlyList<CatalogPerk> perks,
        IReadOnlyList<CatalogPerkStyle> perkStyles,
        IReadOnlyList<CatalogRegalia> regalia,
        IReadOnlyList<CatalogEmote> emotes,
        IReadOnlyList<CatalogIcon> icons,
        IReadOnlyList<CatalogIconSet> iconSets,
        IReadOnlyList<CatalogSpell> spells,
        IReadOnlyList<CatalogWard> wards,
        IReadOnlyList<CatalogWardSet> wardSets)
    {
        Champions = champions;
        Skins = skins;
        Borders = borders;
        SkinLines = skinLines;
        Universes = universes;
        Items = items;
        Perks = perks;
        PerkStyles = perkStyles;
        Regalia = regalia;
        Emotes = emotes;
        Icons = icons;
        IconSets = iconSets;
        Spells = spells;
        Wards = wards;
        WardSets = wardSets;

        foreach (var champion in champions)
        {
            _byAlias[champion.Alias] = champion;
            _byId[champion.Id] = champion;
        }

        foreach (var skin in skins)
        {
            _skinsById[skin.Id] = skin;
            foreach (var path in skin.Paths)
                if (SkinFolderOf(path) is { } folder) _skinFolderIndex.TryAdd(folder, skin.Id);
            foreach (var chroma in skin.Chromas)
            {
                _skinByChroma.TryAdd(chroma.Id, skin.Id);
                foreach (var path in chroma.Paths)
                    if (SkinFolderOf(path) is { } folder) _skinFolderIndex.TryAdd(folder, skin.Id);
            }
        }

        foreach (var line in skinLines) _skinLines[line.Id] = line;
        foreach (var universe in universes)
        {
            _universes[universe.Id] = universe;
            foreach (var line in universe.SkinLines) _universeOfLine.TryAdd(line, universe.Id);
        }

        foreach (var item in items) _items[item.Id] = item;
        foreach (var perk in perks)
        {
            _perks[perk.Id] = perk;
            _perkByFolder.TryAdd(Normalize(perk.Name), perk);
        }

        foreach (var style in perkStyles)
        {
            _perkStyleByFolder.TryAdd(Normalize(style.Name), style);
            foreach (var perk in style.Perks) _perkStyleOf.TryAdd(perk, style.Name);
        }

        foreach (var emote in emotes) _emotes[emote.Id] = emote;
        foreach (var icon in icons) _icons[icon.Id] = icon;
        foreach (var set in iconSets)
        {
            _iconSets[set.Id] = set;
            foreach (var icon in set.Icons) _iconSetOfIcon.TryAdd(icon, set.Id);
        }

        foreach (var spell in spells) _spells[spell.Id] = spell;
        foreach (var ward in wards) _wards[ward.Id] = ward;
        foreach (var set in wardSets)
        {
            _wardSets[set.Id] = set;
            foreach (var ward in set.Wards) _wardSetOfWard.TryAdd(ward, set.Id);
        }

        _aliasesByLength = _byAlias.Keys.OrderByDescending(alias => alias.Length).ToArray();

        BuildReferences();
    }

    public static CatalogTables Empty { get; } = new(
        Array.Empty<CatalogChampion>(), Array.Empty<CatalogSkin>(), Array.Empty<CatalogSkinBorder>(),
        Array.Empty<CatalogSkinLine>(),
        Array.Empty<CatalogUniverse>(), Array.Empty<CatalogItem>(), Array.Empty<CatalogPerk>(),
        Array.Empty<CatalogPerkStyle>(), Array.Empty<CatalogRegalia>(), Array.Empty<CatalogEmote>(),
        Array.Empty<CatalogIcon>(), Array.Empty<CatalogIconSet>(), Array.Empty<CatalogSpell>(),
        Array.Empty<CatalogWard>(), Array.Empty<CatalogWardSet>());

    public IReadOnlyList<CatalogChampion> Champions { get; }

    public IReadOnlyList<CatalogSkin> Skins { get; }

    public IReadOnlyList<CatalogSkinBorder> Borders { get; }

    public IReadOnlyList<CatalogSkinLine> SkinLines { get; }

    public IReadOnlyList<CatalogUniverse> Universes { get; }

    public IReadOnlyList<CatalogItem> Items { get; }

    public IReadOnlyList<CatalogPerk> Perks { get; }

    public IReadOnlyList<CatalogPerkStyle> PerkStyles { get; }

    public IReadOnlyList<CatalogRegalia> Regalia { get; }

    public IReadOnlyList<CatalogEmote> Emotes { get; }

    public IReadOnlyList<CatalogIcon> Icons { get; }

    public IReadOnlyList<CatalogIconSet> IconSets { get; }

    public IReadOnlyList<CatalogSpell> Spells { get; }

    public IReadOnlyList<CatalogWard> Wards { get; }

    public IReadOnlyList<CatalogWardSet> WardSets { get; }

    public bool IsEmpty => Champions.Count == 0 && Skins.Count == 0 && Items.Count == 0;

    /// <summary>The table json exactly as the client ships it, kept so an export can transcribe it.</summary>
    private readonly Dictionary<string, string> _rawTables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The same files per client language folder (<c>default</c>, <c>zh_cn</c>, …).</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _rawByLanguage =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raw json of one client table (<c>skins.json</c>, …) or null when it was not loaded. The copy of
    /// the language the window is in wins — an export reads the names the user just read — and the
    /// English <c>default</c> copy is the fallback.
    /// </summary>
    public bool TryGetTableText(string file, out string json)
    {
        foreach (var folder in new[] { CatalogLanguages.CurrentFolder, CatalogLanguages.EnglishFolder })
            if (_rawByLanguage.TryGetValue(folder, out var bucket) && bucket.TryGetValue(file, out json!))
                return true;

        foreach (var bucket in _rawByLanguage.Values)
            if (bucket.TryGetValue(file, out json!)) return true;

        return _rawTables.TryGetValue(file, out json!);
    }

    private void KeepTables(IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, Dictionary<string, string>>? byLanguage = null)
    {
        foreach (var (file, json) in files) _rawTables[file] = json;
        if (byLanguage is null) return;

        foreach (var (folder, bucket) in byLanguage)
        {
            if (!_rawByLanguage.TryGetValue(folder, out var target))
                _rawByLanguage[folder] = target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, json) in bucket) target[file] = json;
        }
    }

    // ── loading ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the tables out of the client plugin archives. The English (<c>default</c>) copies win for
    /// what the window shows and how entries are classified — names read as the game writes them in
    /// English — while every language folder is kept as it is, so an export can transcribe the copy of
    /// its own language. A client that ships only one of them still classifies.
    /// </summary>
    public static CatalogTables LoadFromClient(
        IReadOnlyList<WadSummary> clientWads,
        NameTable clientNames,
        IProgress<string>? log = null)
    {
        var byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var found = new List<(string Path, CatalogSection Section, string File)>();

        foreach (var wad in clientWads)
        {
            try
            {
                using var archive = WadArchive.Open(wad.FullPath);
                foreach (var entry in archive.ReadEntries(clientNames))
                {
                    if (entry.Path is not { } path) continue;
                    var file = FileNameOf(path);
                    if (!TableSections.TryGetValue(file, out var section)) continue;

                    var folder = CatalogLanguages.FolderOfPath(path);
                    if (folder.Length == 0) continue;

                    found.Add((path, section, file));
                    if (!byLanguage.TryGetValue(folder, out var bucket))
                        byLanguage[folder] = bucket = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (bucket.ContainsKey(file)) continue;

                    var bytes = ReadAll(archive, entry.Hash);
                    if (bytes is null) continue;
                    bucket[file] = System.Text.Encoding.UTF8.GetString(bytes);
                }
            }
            catch (Exception ex)
            {
                log?.Report(Strings.T("Core.Tables.ReadFailed", wad.Name, ex.Message));
            }
        }

        // 界面与分类认英文那份，缺了再用别的语言顶上。
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (byLanguage.TryGetValue(CatalogLanguages.EnglishFolder, out var english))
            foreach (var (file, json) in english) files[file] = json;
        foreach (var bucket in byLanguage.Values)
            foreach (var (file, json) in bucket) files.TryAdd(file, json);

        if (files.Count == 0)
        {
            log?.Report(Strings.T("Core.Tables.Missing"));
            return Empty;
        }

        var tables = FromJson(files, byLanguage);
        tables._tableFiles.AddRange(found);
        log?.Report(Strings.T("Core.Tables.Summary", files.Count, tables.Champions.Count, tables.Skins.Count,
            tables.Items.Count, tables.Icons.Count, tables.Emotes.Count));
        return tables;
    }

    /// <summary>
    /// Reads the tables from raw json, keyed by file name (for tests and the CLI).
    /// <paramref name="byLanguage"/> optionally carries the same files per client language folder, so
    /// <see cref="TryGetTableText"/> can hand an export the copy of its own language.
    /// </summary>
    public static CatalogTables FromJson(IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, Dictionary<string, string>>? byLanguage = null)
    {
        var champions = ParseChampions(files);
        var borders = ParseSkinBorders(files);
        var skinLines = ParseSkinLines(files);
        var universes = ParseUniverses(files);
        var skins = ParseSkins(files);
        var items = ParseItems(files);
        var perks = ParsePerks(files);
        var perkStyles = ParsePerkStyles(files);
        var regalia = ParseRegalia(files);
        var emotes = ParseEmotes(files);
        var icons = ParseIcons(files);
        var iconSets = ParseIconSets(files);
        var spells = ParseSpells(files);
        var wards = ParseWards(files);
        var wardSets = ParseWardSets(files);

        var tables = new CatalogTables(champions, skins, borders, skinLines, universes, items, perks, perkStyles,
            regalia, emotes, icons, iconSets, spells, wards, wardSets);
        tables.KeepTables(files, byLanguage);   // 导出的 json 用当前语言那份
        return tables;
    }

    private static byte[]? ReadAll(WadArchive archive, ulong hash)
    {
        try
        {
            using var stream = archive.OpenDecompressed(hash);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── json parsing ────────────────────────────────────────────────────────────────────────

    private static IEnumerable<JsonElement> Objects(IReadOnlyDictionary<string, string> files, string file)
    {
        if (!files.TryGetValue(file, out var json) || string.IsNullOrWhiteSpace(json)) yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object) yield return item.Clone();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // skins.json is an object keyed by id; perkstyles.json wraps its array in "styles".
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object) yield return property.Value.Clone();
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                        foreach (var item in property.Value.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object) yield return item.Clone();
                }
            }
        }
    }

    private static List<CatalogChampion> ParseChampions(IReadOnlyDictionary<string, string> files)
    {
        var champions = new List<CatalogChampion>();
        foreach (var item in Objects(files, "champion-summary.json"))
        {
            var id = Int(item, "id");
            var alias = Str(item, "alias");
            if (id <= 0 || string.IsNullOrWhiteSpace(alias)) continue;
            champions.Add(new CatalogChampion(
                id,
                alias.Trim(),
                Str(item, "name") ?? alias,
                Str(item, "description") ?? string.Empty,
                Str(item, "squarePortraitPath")));
        }

        return champions;
    }

    private static List<CatalogSkinLine> ParseSkinLines(IReadOnlyDictionary<string, string> files)
    {
        var lines = new List<CatalogSkinLine>();
        foreach (var item in Objects(files, "skinlines.json"))
        {
            var id = Int(item, "id");
            if (id <= 0) continue;
            lines.Add(new CatalogSkinLine(id, Str(item, "name") ?? id.ToString(), Clean(Str(item, "description"))));
        }

        return lines;
    }

    private static List<CatalogUniverse> ParseUniverses(IReadOnlyDictionary<string, string> files)
    {
        var universes = new List<CatalogUniverse>();
        foreach (var item in Objects(files, "universes.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var sets = new List<int>();
            if (item.TryGetProperty("skinSets", out var skinSets) && skinSets.ValueKind == JsonValueKind.Array)
                foreach (var set in skinSets.EnumerateArray())
                    if (set.ValueKind == JsonValueKind.Number && set.TryGetInt32(out var value)) sets.Add(value);

            universes.Add(new CatalogUniverse(id, name.Trim(), Clean(Str(item, "description")), Str(item, "imagePath"), sets));
        }

        return universes;
    }

    private static List<CatalogSkin> ParseSkins(IReadOnlyDictionary<string, string> files)
    {
        var skins = new List<CatalogSkin>();
        foreach (var item in Objects(files, "skins.json"))
        {
            var id = Int(item, "id");
            if (id <= 0) continue;

            var paths = new List<string>();
            foreach (var field in SkinPathFields)
                if (Str(item, field) is { } path) paths.Add(path);

            var chromas = new List<CatalogChroma>();
            if (item.TryGetProperty("chromas", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var chroma in array.EnumerateArray())
                {
                    if (chroma.ValueKind != JsonValueKind.Object) continue;
                    var chromaId = Int(chroma, "id");
                    var chromaPaths = new List<string>();
                    foreach (var field in new[] { "chromaPath", "tilePath" })
                        if (Str(chroma, field) is { } path) chromaPaths.Add(path);
                    chromas.Add(new CatalogChroma(chromaId, Str(chroma, "name") ?? string.Empty, chromaPaths));
                }
            }

            var skinLine = 0;
            if (item.TryGetProperty("skinLines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object) continue;
                    var value = Int(line, "id");
                    if (value > 0)
                    {
                        skinLine = value;
                        break;
                    }
                }
            }

            skins.Add(new CatalogSkin(
                id,
                id / 1000,
                Str(item, "name") ?? id.ToString(),
                Bool(item, "isBase"),
                Clean(Str(item, "description")),
                skinLine,
                paths,
                chromas));
        }

        return skins;
    }

    private static List<CatalogSkinBorder> ParseSkinBorders(IReadOnlyDictionary<string, string> files)
    {
        var borders = new List<CatalogSkinBorder>();
        foreach (var item in Objects(files, "skinborders.json"))
        {
            var name = Str(item, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            borders.Add(new CatalogSkinBorder(name.Trim(), Str(item, "image")));
        }

        return borders;
    }

    private static List<CatalogItem> ParseItems(IReadOnlyDictionary<string, string> files)
    {
        var items = new List<CatalogItem>();
        foreach (var item in Objects(files, "items.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var categories = new List<string>();
            if (item.TryGetProperty("categories", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var category in array.EnumerateArray())
                    if (category.ValueKind == JsonValueKind.String && category.GetString() is { Length: > 0 } value)
                        categories.Add(value);

            items.Add(new CatalogItem(id, name.Trim(), Clean(Str(item, "description")), categories, Str(item, "iconPath")));
        }

        return items;
    }

    private static List<CatalogPerk> ParsePerks(IReadOnlyDictionary<string, string> files)
    {
        var perks = new List<CatalogPerk>();
        foreach (var item in Objects(files, "perks.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;
            var description = Clean(Str(item, "shortDesc")) ?? Clean(Str(item, "tooltip"));
            perks.Add(new CatalogPerk(id, name.Trim(), description, Str(item, "iconPath")));
        }

        return perks;
    }

    private static List<CatalogPerkStyle> ParsePerkStyles(IReadOnlyDictionary<string, string> files)
    {
        var styles = new List<CatalogPerkStyle>();
        foreach (var item in Objects(files, "perkstyles.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var images = new List<string>();
            if (Str(item, "iconPath") is { } icon) images.Add(icon);
            if (item.TryGetProperty("assetMap", out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var property in map.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } path)
                        images.Add(path);

            var perks = new List<int>();
            if (item.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
            {
                foreach (var slot in slots.EnumerateArray())
                {
                    if (slot.ValueKind != JsonValueKind.Object) continue;
                    if (!slot.TryGetProperty("perks", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                    foreach (var perk in list.EnumerateArray())
                        if (perk.ValueKind == JsonValueKind.Number && perk.TryGetInt32(out var value)) perks.Add(value);
                }
            }

            styles.Add(new CatalogPerkStyle(id, name.Trim(), Clean(Str(item, "tooltip")), Str(item, "iconPath"), images, perks));
        }

        return styles;
    }

    private static List<CatalogRegalia> ParseRegalia(IReadOnlyDictionary<string, string> files)
    {
        var regalia = new List<CatalogRegalia>();
        foreach (var item in Objects(files, "regalia.json"))
        {
            var name = Str(item, "localizedName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var id = item.TryGetProperty("id", out var idValue) ? idValue.ToString() : string.Empty;
            regalia.Add(new CatalogRegalia(
                id,
                name.Trim(),
                Clean(Str(item, "localizedDescription")),
                Str(item, "regaliaType") ?? string.Empty,
                Str(item, "assetPath")));
        }

        return regalia;
    }

    private static List<CatalogEmote> ParseEmotes(IReadOnlyDictionary<string, string> files)
    {
        var emotes = new List<CatalogEmote>();
        foreach (var item in Objects(files, "summoner-emotes.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;
            emotes.Add(new CatalogEmote(id, name.Trim(), Clean(Str(item, "description")), Str(item, "inventoryIcon")));
        }

        return emotes;
    }

    private static List<CatalogIcon> ParseIcons(IReadOnlyDictionary<string, string> files)
    {
        var icons = new List<CatalogIcon>();
        foreach (var item in Objects(files, "summoner-icons.json"))
        {
            var id = Int(item, "id");
            if (id <= 0) continue;
            var description = Clean(Str(item, "description")) ?? FirstDescription(item);
            icons.Add(new CatalogIcon(id, Str(item, "title") ?? id.ToString(), description, Str(item, "imagePath")));
        }

        return icons;
    }

    private static List<CatalogIconSet> ParseIconSets(IReadOnlyDictionary<string, string> files)
    {
        var sets = new List<CatalogIconSet>();
        foreach (var item in Objects(files, "summoner-icon-sets.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "displayName");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var icons = new List<int>();
            if (item.TryGetProperty("icons", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var icon in array.EnumerateArray())
                    if (icon.ValueKind == JsonValueKind.Number && icon.TryGetInt32(out var value)) icons.Add(value);

            sets.Add(new CatalogIconSet(id, name.Trim(), Clean(Str(item, "description")), icons));
        }

        return sets;
    }

    private static List<CatalogSpell> ParseSpells(IReadOnlyDictionary<string, string> files)
    {
        var spells = new List<CatalogSpell>();
        foreach (var item in Objects(files, "summoner-spells.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var modes = new List<string>();
            if (item.TryGetProperty("gameModes", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var mode in array.EnumerateArray())
                    if (mode.ValueKind == JsonValueKind.String && mode.GetString() is { Length: > 0 } value) modes.Add(value);

            spells.Add(new CatalogSpell(id, name.Trim(), Clean(Str(item, "description")), Str(item, "iconPath"), modes));
        }

        return spells;
    }

    private static List<CatalogWard> ParseWards(IReadOnlyDictionary<string, string> files)
    {
        var wards = new List<CatalogWard>();
        foreach (var item in Objects(files, "ward-skins.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var images = new List<string>();
            foreach (var field in new[] { "wardImagePath", "wardShadowImagePath" })
                if (Str(item, field) is { } path) images.Add(path);

            wards.Add(new CatalogWard(id, name.Trim(), Clean(Str(item, "description")), images));
        }

        return wards;
    }

    private static List<CatalogWardSet> ParseWardSets(IReadOnlyDictionary<string, string> files)
    {
        var sets = new List<CatalogWardSet>();
        foreach (var item in Objects(files, "ward-skin-sets.json"))
        {
            var id = Int(item, "id");
            var name = Str(item, "displayName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var wards = new List<int>();
            if (item.TryGetProperty("wards", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var ward in array.EnumerateArray())
                    if (ward.ValueKind == JsonValueKind.Number && ward.TryGetInt32(out var value)) wards.Add(value);

            sets.Add(new CatalogWardSet(id, name.Trim(), Clean(Str(item, "description")), wards));
        }

        return sets;
    }

    // ── reference targets ───────────────────────────────────────────────────────────────────

    /// <summary>Where a skin's images live: its series group, then the skin itself.</summary>
    internal CatalogPlacement SkinTarget(CatalogNodes nodes, CatalogSkin skin)
    {
        var (label, detail, rank) = SeriesOf(skin);
        var group = nodes.Group(CatalogSection.Skin, label, detail, rank);
        var entity = nodes.Entity(group, skin.Name, SkinEntityFolder(skin), skin.Id % 1000, skin.Description);
        return new CatalogPlacement(group, entity);
    }

    /// <summary>
    /// Folder an entity uses below its group, and therefore its export folder. Base and numbered
    /// skins repeat across champions, so the champion alias is kept in front.
    /// </summary>
    internal string SkinEntityFolder(CatalogSkin skin)
    {
        var champion = _byId.TryGetValue(skin.ChampionId, out var found)
            ? found.Alias.ToLowerInvariant()
            : skin.ChampionId.ToString();
        return champion + "_" + SkinFolderOfId(skin.Id);
    }

    internal CatalogPlacement BorderTarget(CatalogNodes nodes, CatalogSkinBorder border)
    {
        var group = nodes.Group(CatalogSection.Skin, "Other", null, 1);
        var entity = nodes.Entity(group, "Borders", "borders", 20000, Strings.T("Core.Tables.BordersDetail"));
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement ChampionIconTarget(CatalogNodes nodes, CatalogChampion champion)
    {
        var group = nodes.Group(CatalogSection.Champion, champion.Label, DetailOf(champion), 0, champion.Alias.ToLowerInvariant());
        var entity = nodes.Entity(group, "Icon", "icon", 10001);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement UniverseImageTarget(CatalogNodes nodes, CatalogUniverse universe)
    {
        var group = nodes.Group(CatalogSection.Skin, universe.Name, universe.Description, 0);
        var entity = nodes.Entity(group, "Image", "image", 20000, universe.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement ItemTarget(CatalogNodes nodes, CatalogItem item)
    {
        var category = item.Categories.FirstOrDefault(category => !string.IsNullOrWhiteSpace(category)) ?? "Other";
        var group = nodes.Group(CatalogSection.Item, category, null, category == "Other" ? (byte)1 : (byte)0);
        var entity = nodes.Entity(group, item.Name, CatalogPaths.SafeFolderOf(item.Name), 0, item.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement PerkTarget(CatalogNodes nodes, CatalogPerk perk)
    {
        var style = _perkStyleOf.TryGetValue(perk.Id, out var name)
            ? name
            : perk.IconPath?.Contains("/statmods/", StringComparison.OrdinalIgnoreCase) == true ? "StatMods" : "Perks";
        var detail = _perkStyleByFolder.TryGetValue(Normalize(style), out var info) ? info.Description : null;
        var group = nodes.Group(CatalogSection.Perk, style, detail, style is "Perks" or "StatMods" ? (byte)1 : (byte)0);
        var entity = nodes.Entity(group, perk.Name, CatalogPaths.SafeFolderOf(perk.Name), 0, perk.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement PerkStyleTarget(CatalogNodes nodes, CatalogPerkStyle style)
    {
        var group = nodes.Group(CatalogSection.Perk, style.Name, style.Description, 0);
        var entity = nodes.Entity(group, "Icons", "icons", 1);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement RegaliaTarget(CatalogNodes nodes, CatalogRegalia regalia)
    {
        var banner = string.Equals(regalia.Type, "kBanner", StringComparison.OrdinalIgnoreCase);
        var group = nodes.Group(CatalogSection.Regalia, banner ? "Banner" : "Other", null, banner ? (byte)0 : (byte)1);
        var entity = nodes.Entity(group, regalia.Name, CatalogPaths.SafeFolderOf(regalia.Name), 0, regalia.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement EmoteTarget(CatalogNodes nodes, CatalogEmote emote)
    {
        var folder = EmoteFolderOf(emote.IconPath);
        var group = nodes.Group(CatalogSection.Emote, folder, null, folder == "Other" ? (byte)1 : (byte)0);
        var entity = nodes.Entity(group, emote.Name, CatalogPaths.SafeFolderOf(emote.Name), 0, emote.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement IconTarget(CatalogNodes nodes, CatalogIcon icon)
    {
        CatalogIconSet? set = null;
        if (_iconSetOfIcon.TryGetValue(icon.Id, out var setId)) _iconSets.TryGetValue(setId, out set);
        var group = nodes.Group(CatalogSection.Icon, set?.Name ?? "Other", set?.Description, set is null ? (byte)1 : (byte)0);
        var entity = nodes.Entity(group, icon.Title, CatalogPaths.SafeFolderOf(icon.Title), 0, icon.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement SpellTarget(CatalogNodes nodes, CatalogSpell spell)
    {
        var group = nodes.Group(CatalogSection.SummonerSpell, "All", null, 1);
        var entity = nodes.Entity(group, spell.Name, CatalogPaths.SafeFolderOf(spell.Name), 0, spell.Description);
        return new CatalogPlacement(group, entity);
    }

    internal CatalogPlacement WardTarget(CatalogNodes nodes, CatalogWard ward)
    {
        CatalogWardSet? set = null;
        if (_wardSetOfWard.TryGetValue(ward.Id, out var setId)) _wardSets.TryGetValue(setId, out set);
        var group = nodes.Group(CatalogSection.Ward, set?.Name ?? "Other", set?.Description, set is null ? (byte)1 : (byte)0);
        var entity = nodes.Entity(group, ward.Name, CatalogPaths.SafeFolderOf(ward.Name), 0, ward.Description);
        return new CatalogPlacement(group, entity);
    }

    /// <summary>Series label of a skin: "Universe · Skin line", one of them, or Base / Other.</summary>
    internal (string Label, string? Detail, byte Rank) SeriesOf(CatalogSkin skin)
    {
        if (skin.SkinLine > 0 && _skinLines.TryGetValue(skin.SkinLine, out var line))
        {
            var universe = _universeOfLine.TryGetValue(line.Id, out var universeId)
                           && _universes.TryGetValue(universeId, out var found)
                           && !string.IsNullOrWhiteSpace(found.Name)
                ? found.Name
                : null;
            var label = universe is not null && !string.Equals(universe, line.Name, StringComparison.OrdinalIgnoreCase)
                ? $"{universe} · {line.Name}"
                : line.Name;
            return (label, line.Description, 0);
        }

        return (skin.IsBase ? "Base" : "Other", null, 1);
    }

    private void BuildReferences()
    {
        foreach (var champion in Champions)
        {
            if (champion.PortraitPath is { } portrait)
                _references.Add((portrait, nodes => ChampionIconTarget(nodes, champion)));
        }

        foreach (var skin in Skins)
        {
            Func<CatalogNodes, CatalogPlacement> target = nodes => SkinTarget(nodes, skin);
            foreach (var path in skin.Paths) _references.Add((path, target));
            foreach (var chroma in skin.Chromas)
                foreach (var path in chroma.Paths)
                    _references.Add((path, target));
        }

        foreach (var border in Borders)
        {
            var skin = SkinForImage(border.Image);
            if (skin is not null) _references.Add((border.Image!, nodes => SkinTarget(nodes, skin)));
            else _references.Add((border.Image!, nodes => BorderTarget(nodes, border)));
        }

        foreach (var universe in Universes)
            if (universe.ImagePath is { Length: > 0 } image)
                _references.Add((image, nodes => UniverseImageTarget(nodes, universe)));

        foreach (var item in Items)
            if (item.IconPath is { } icon)
                _references.Add((icon, nodes => ItemTarget(nodes, item)));

        foreach (var perk in Perks)
            if (perk.IconPath is { } icon)
                _references.Add((icon, nodes => PerkTarget(nodes, perk)));

        foreach (var style in PerkStyles)
            foreach (var image in style.Images)
                _references.Add((image, nodes => PerkStyleTarget(nodes, style)));

        foreach (var regalia in Regalia)
            if (regalia.AssetPath is { } asset)
                _references.Add((asset, nodes => RegaliaTarget(nodes, regalia)));

        foreach (var emote in Emotes)
            if (emote.IconPath is { } icon)
                _references.Add((icon, nodes => EmoteTarget(nodes, emote)));

        foreach (var icon in Icons)
            if (icon.ImagePath is { } image)
                _references.Add((image, nodes => IconTarget(nodes, icon)));

        foreach (var spell in Spells)
            if (spell.IconPath is { } icon)
                _references.Add((icon, nodes => SpellTarget(nodes, spell)));

        foreach (var ward in Wards)
            foreach (var image in ward.Images)
                _references.Add((image, nodes => WardTarget(nodes, ward)));
    }

    /// <summary>
    /// Resolves every reference the tables make into a hash → (group, entity) map. The candidates of
    /// each <c>/lol-game-data/…</c> value are hashed, so a client path that differs only in case or in
    /// its <c>assets/</c> prefix still resolves; a reference whose file is not in any local wad
    /// simply produces no row.
    /// </summary>
    internal IReadOnlyDictionary<ulong, CatalogPlacement> BuildReferenceIndex(CatalogNodes nodes, IProgress<string>? log = null)
    {
        var index = new Dictionary<ulong, CatalogPlacement>(Math.Max(16, _references.Count * 2));

        foreach (var (path, target) in _references)
        {
            if (!CatalogPaths.LooksLikeFile(path)) continue;
            var placement = target(nodes);
            foreach (var candidate in CatalogPaths.ClientCandidates(path))
                index.TryAdd(PathHashing.Compute(candidate), placement);
        }

        foreach (var (path, section, file) in _tableFiles)
        {
            var group = nodes.Group(section, "Tables", Strings.T("Core.Tables.GroupDetail"), 1, "tables");
            var entity = nodes.Entity(group, file, CatalogPaths.SafeFolderOf(Path.GetFileNameWithoutExtension(file)), TablesRank);
            index.TryAdd(PathHashing.Compute(path), new CatalogPlacement(group, entity));
        }

        log?.Report(Strings.T("Core.Tables.References", index.Count));
        return index;
    }

    private CatalogSkin? SkinForImage(string? image)
    {
        if (SkinFolderOf(image) is not { } folder) return null;
        return _skinFolderIndex.TryGetValue(folder, out var id) && _skinsById.TryGetValue(id, out var skin) ? skin : null;
    }

    // ── lookups ─────────────────────────────────────────────────────────────────────────────

    public bool TryGetChampionByAlias(string? alias, out CatalogChampion champion)
    {
        champion = null!;
        return !string.IsNullOrWhiteSpace(alias) && _byAlias.TryGetValue(alias.Trim(), out champion!);
    }

    public bool TryGetChampionById(int id, out CatalogChampion champion)
        => _byId.TryGetValue(id, out champion!);

    public bool TryGetSkin(int id, out CatalogSkin skin) => _skinsById.TryGetValue(id, out skin!);

    /// <summary>Resolves a skin from the folder pair found on an asset path.</summary>
    public bool TryGetSkinByFolder(string championFolder, string skinFolder, out CatalogSkin skin)
    {
        skin = null!;
        return !string.IsNullOrWhiteSpace(championFolder)
               && !string.IsNullOrWhiteSpace(skinFolder)
               && _skinFolderIndex.TryGetValue(championFolder + "|" + skinFolder, out var id)
               && _skinsById.TryGetValue(id, out skin!);
    }

    /// <summary>Skin a chroma image belongs to.</summary>
    public bool TryGetSkinOfChroma(int chromaId, out CatalogSkin skin)
    {
        skin = null!;
        return _skinByChroma.TryGetValue(chromaId, out var id) && _skinsById.TryGetValue(id, out skin!);
    }

    public bool TryGetItem(int id, out CatalogItem item) => _items.TryGetValue(id, out item!);

    public bool TryGetSpell(int id, out CatalogSpell spell) => _spells.TryGetValue(id, out spell!);

    /// <summary>Rune whose name, ignoring case and punctuation, is the given folder name.</summary>
    public bool TryGetPerkByFolder(string folder, out CatalogPerk perk)
        => _perkByFolder.TryGetValue(Normalize(folder), out perk!);

    public bool TryGetPerkStyleByFolder(string folder, out CatalogPerkStyle style)
        => _perkStyleByFolder.TryGetValue(Normalize(folder), out style!);

    /// <summary>
    /// Best effort champion for a folder name: exact alias first, then the alias after dropping a
    /// known mode prefix (<c>jade_</c>, <c>tft17_</c>, <c>pet</c>), then a name that starts or ends
    /// with one. Anything past the exact match is reported as guessed.
    /// </summary>
    public bool TryGuessChampion(string? folder, out CatalogChampion champion, out bool guessed)
    {
        champion = null!;
        guessed = false;
        if (string.IsNullOrWhiteSpace(folder)) return false;

        if (_byAlias.TryGetValue(folder, out champion!)) return true;

        foreach (var candidate in StripModePrefixes(folder))
        {
            if (candidate.Length < 3) continue;
            if (_byAlias.TryGetValue(candidate, out champion!))
            {
                guessed = true;
                return true;
            }
        }

        foreach (var candidate in StripModePrefixes(folder))
        {
            if (candidate.Length < 4) continue;
            foreach (var alias in _aliasesByLength)
            {
                if (alias.Length < 3) continue;

                // "zedshadow" starts with zed, "cherry_goh_kindred" ends with kindred.
                var starts = candidate.StartsWith(alias, StringComparison.OrdinalIgnoreCase);
                var ends = candidate.EndsWith(alias, StringComparison.OrdinalIgnoreCase)
                           && candidate.Length > alias.Length
                           && (candidate[^(alias.Length + 1)] is '_' or '-');
                if (!starts && !ends) continue;

                champion = _byAlias[alias];
                guessed = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Variants of a folder name once the mode prefixes are gone.</summary>
    private static IEnumerable<string> StripModePrefixes(string folder)
    {
        yield return folder;

        var lowered = folder.ToLowerInvariant();
        foreach (var prefix in new[] { "jade_", "jade", "pet_", "pet" })
        {
            if (!lowered.StartsWith(prefix, StringComparison.Ordinal)) continue;
            yield return folder[prefix.Length..];
        }

        // "tft17_jinx", "tft9_doubleup_ahri": drop a leading tft and its set number.
        if (lowered.StartsWith("tft", StringComparison.Ordinal))
        {
            var rest = lowered[3..];
            var digits = 0;
            while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
            var tail = rest[digits..].TrimStart('_', '-');
            if (tail.Length >= 3) yield return tail;
        }

        // "set11_ahri", "cherry_goh_kindred" style prefixes keep the last two segments.
        var parts = lowered.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2)
            yield return string.Join('_', parts[^2..]);
    }

    // ── path helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>Json fields of a skin that point at one of its images.</summary>
    private static readonly string[] SkinPathFields =
    {
        "splashPath",
        "uncenteredSplashPath",
        "tilePath",
        "loadScreenPath",
        "loadScreenVintagePath",
        "splashVideoPath",
        "collectionSplashVideoPath",
        "collectionCardHoverVideoPath",
        "chromaPath",
    };

    /// <summary>"…/characters/ahri/skins/skin05/images/x.jpg" becomes "ahri|skin05".</summary>
    public static string? SkinFolderOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var lowered = path.ToLowerInvariant();
        var characters = lowered.LastIndexOf(CharactersMarker, StringComparison.Ordinal);
        if (characters < 0) return null;
        var start = characters + CharactersMarker.Length;
        var skins = lowered.IndexOf(SkinsMarker, start, StringComparison.Ordinal);
        if (skins < 0) return null;

        var champion = lowered[start..skins];
        var skinStart = skins + SkinsMarker.Length;
        var skinEnd = lowered.IndexOf('/', skinStart);
        if (skinEnd < 0 || champion.Length == 0) return null;
        return champion + "|" + lowered[skinStart..skinEnd];
    }

    /// <summary>The champion folder of an asset path, e.g. "ahri" for "assets/characters/ahri/…".</summary>
    public static string? ChampionFolderOf(string path)
    {
        var index = path.IndexOf(CharactersMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + CharactersMarker.Length;
        var end = path.IndexOf('/', start);
        if (end <= start) return null;
        return path[start..end];
    }

    /// <summary>The skin folder of an asset path, e.g. "skin05" for "…/skins/skin05/…".</summary>
    public static string? SkinFolderIn(string path)
    {
        var index = path.IndexOf(SkinsMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + SkinsMarker.Length;
        var end = path.IndexOf('/', start);
        return end <= start ? null : path[start..end];
    }

    /// <summary>Folder name a numeric skin id uses in the game files: 103005 → skin05, 103000 → base.</summary>
    public static string SkinFolderOfId(int skinId)
    {
        var number = skinId % 1000;
        return number == 0 ? "base" : $"skin{number:D2}";
    }

    /// <summary>Ui label of a skin folder nothing in the tables names.</summary>
    public static string SkinFolderLabel(string folder)
    {
        if (string.Equals(folder, "base", StringComparison.OrdinalIgnoreCase)) return "Base";
        if (folder.StartsWith("skin", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(folder.AsSpan(4), out var number) && number > 0)
            return $"Skin {number:D2}";
        return folder;
    }

    /// <summary>Skin number of a folder name inside one champion, validated against the tables.</summary>
    public static bool TryNumericSkinId(int championId, string folder, out int id)
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

    /// <summary>Folder of an emote's icon below <c>SummonerEmotes</c>: "TFT", "Champions", …</summary>
    public static string EmoteFolderOf(string? iconPath)
    {
        const string marker = "summoneremotes/";
        if (string.IsNullOrWhiteSpace(iconPath)) return "Other";
        var index = iconPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return "Other";
        var start = index + marker.Length;
        var end = iconPath.IndexOf('/', start);
        if (end <= start) return "Other";
        return iconPath[start..end];
    }

    /// <summary>"Resolve" or "Dark Harvest" → "resolve" / "darkharvest".</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    internal static string DetailOf(CatalogChampion champion)
        => string.IsNullOrWhiteSpace(champion.Description)
            ? champion.Name
            : $"{champion.Name} · {champion.Description}";

    /// <summary>Drops the markup the game's tool tips use, so a label or a tool tip stays readable.</summary>
    internal static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var builder = new StringBuilder(text.Length);
        var inside = false;
        foreach (var c in text)
        {
            if (c == '<')
            {
                inside = true;
                continue;
            }

            if (c == '>')
            {
                inside = false;
                continue;
            }

            if (!inside) builder.Append(c);
        }

        var result = builder.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (result.Contains("  ", StringComparison.Ordinal)) result = result.Replace("  ", " ");
        return result.Length == 0 ? null : result;
    }

    private static string? FirstDescription(JsonElement item)
    {
        if (!item.TryGetProperty("descriptions", out var array) || array.ValueKind != JsonValueKind.Array) return null;
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (Str(entry, "description") is { Length: > 0 } value) return Clean(value);
        }

        return null;
    }

    private static int Int(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static bool Bool(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FileNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
