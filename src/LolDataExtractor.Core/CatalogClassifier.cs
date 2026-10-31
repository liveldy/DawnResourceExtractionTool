namespace LolDataExtractor.Core;

/// <summary>
/// The groups and entities of one catalog build. Both the client tables and the path rules register
/// into it, so a path reached either way lands on the same node.
/// </summary>
internal sealed class CatalogNodes
{
    private readonly List<CatalogGroup> _groups = new();
    private readonly List<CatalogEntity> _entities = new();
    private readonly Dictionary<string, int> _groupKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _entityKeys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CatalogGroup> Groups => _groups;

    public IReadOnlyList<CatalogEntity> Entities => _entities;

    /// <summary>Index of a group, creating it when the section does not have it yet.</summary>
    public int Group(CatalogSection section, string label, string? detail = null, byte rank = 0, string? folder = null)
    {
        var name = CatalogPaths.SafeFolderOf(string.IsNullOrWhiteSpace(folder) ? label : folder);
        var key = (int)section + "|" + name;
        if (_groupKeys.TryGetValue(key, out var existing)) return existing;

        var index = _groups.Count;
        if (index >= ushort.MaxValue) return 0;
        _groups.Add(new CatalogGroup(section, label, name, detail, rank));
        _groupKeys[key] = index;
        return index;
    }

    /// <summary>Index of an entity inside a group, creating it when the group does not have it yet.</summary>
    public int Entity(int group, string label, string folder, int rank, string? detail = null)
    {
        var name = CatalogPaths.SafeFolderOf(folder);
        var key = group + "|" + name;
        if (_entityKeys.TryGetValue(key, out var existing)) return existing;

        var index = _entities.Count;
        if (index >= ushort.MaxValue) return 0;
        _entities.Add(new CatalogEntity(group, label, name, detail, rank));
        _entityKeys[key] = index;
        return index;
    }
}

/// <summary>
/// Turns an asset path into a section, a group and an entity. What the client tables say always wins
/// (it is applied before this class runs); the rules below cover the game archives and the client
/// paths the tables do not name. A path no rule claims stays out of the catalog.
/// </summary>
internal sealed class CatalogClassifier
{
    private const int CommonRank = 10000;
    private const int IconRank = 10001;
    private const int DataRank = 10002;
    private const int VoiceRank = 10003;
    private const int SfxRank = 10004;
    private const int AudioRank = 10005;

    private readonly CatalogTables _tables;
    private readonly CatalogOverrides _overrides;
    private readonly IReadOnlyList<CatalogRule> _rules;
    private readonly CatalogNodes _nodes;
    private readonly Dictionary<string, int> _championGroups = new(StringComparer.OrdinalIgnoreCase);

    public CatalogClassifier(CatalogTables tables, CatalogOverrides overrides, CatalogNodes nodes)
    {
        _tables = tables;
        _overrides = overrides;
        _nodes = nodes;
        _rules = overrides.RulesBySpecificity();
    }

    public CatalogNodes Nodes => _nodes;

    /// <summary>Place of one path in the tree; false when no section owns it.</summary>
    public bool TryAssign(string path, out CatalogPlacement placement)
    {
        placement = default;
        if (string.IsNullOrEmpty(path)) return false;

        var lowered = path.ToLowerInvariant();
        return lowered.StartsWith("plugins/", StringComparison.Ordinal)
            ? TryAssignClient(lowered, out placement)
            : TryAssignGame(lowered, out placement);
    }

    /// <summary>
    /// Applies the hand written rules. Returns false when the entry is hidden; otherwise the
    /// placement may be moved to another section, group or entity.
    /// </summary>
    public bool Apply(ulong hash, string path, ref CatalogPlacement placement)
    {
        var entry = _overrides.EntryOf(hash);
        CatalogRule? rule = null;
        foreach (var candidate in _rules)
        {
            if (!path.StartsWith(candidate.PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            rule = candidate;
            break;
        }

        if (entry is null && rule is null) return true;
        if (entry?.Hidden == true || rule?.Hidden == true) return false;

        var sectionText = entry?.Section ?? rule?.Section;
        var groupText = entry?.Group ?? rule?.Group;
        var entityText = entry?.Entity ?? rule?.Entity;
        if (string.IsNullOrWhiteSpace(sectionText) && string.IsNullOrWhiteSpace(groupText) && string.IsNullOrWhiteSpace(entityText))
            return true;

        var automaticGroup = _nodes.Groups[placement.Group];
        var section = automaticGroup.Section;
        if (CatalogSectionInfo.TryParse(sectionText, out var parsed)) section = parsed;

        var groupLabel = string.IsNullOrWhiteSpace(groupText) ? automaticGroup.Label : groupText.Trim();
        var groupDetail = automaticGroup.Detail;
        var groupRank = automaticGroup.Rank;

        // A champion alias typed by hand lands on the table's own label, not on the typed casing.
        if (section == CatalogSection.Champion && _tables.TryGetChampionByAlias(groupLabel, out var champion))
        {
            groupLabel = champion.Alias;
            groupDetail = CatalogTables.DetailOf(champion);
            groupRank = 0;
        }

        var group = _nodes.Group(section, groupLabel, groupDetail, groupRank,
            CatalogPaths.SafeFolderOf(groupLabel));

        int entity;
        if (!string.IsNullOrWhiteSpace(entityText))
        {
            var label = entityText.Trim();
            entity = _nodes.Entity(group, label, CatalogPaths.SafeFolderOf(label), 0);
        }
        else if (group == placement.Group && section == automaticGroup.Section)
        {
            entity = placement.Entity;
        }
        else
        {
            var automaticEntity = _nodes.Entities[placement.Entity];
            entity = _nodes.Entity(group, automaticEntity.Label, automaticEntity.Folder, automaticEntity.Rank, automaticEntity.Detail);
        }

        placement = new CatalogPlacement(group, entity);
        return true;
    }

    // ── client archives ─────────────────────────────────────────────────────────────────────

    private bool TryAssignClient(string path, out CatalogPlacement placement)
    {
        placement = default;
        const string prefix = "plugins/rcp-be-lol-game-data/global/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash < 0) return false;
        var body = rest[(slash + 1)..];

        // v1/champions/<id>.json: the champion's own data, English and Chinese copies both land here.
        if (body.StartsWith("v1/champions/", StringComparison.Ordinal) && body.EndsWith(".json", StringComparison.Ordinal))
            return ChampionEntity(LeadingNumber(FileNameOf(body)), DataRank, "Data", "data", out placement);

        // v1/champion-icons/<id>.png: the square portrait used in the client.
        if (body.StartsWith("v1/champion-icons/", StringComparison.Ordinal))
            return ChampionEntity(LeadingNumber(FileNameOf(body)), IconRank, "Icon", "icon", out placement);

        // v1/champion-chroma-images/<championId>/<chromaId>.png: a chroma belongs to its skin.
        if (body.StartsWith("v1/champion-chroma-images/", StringComparison.Ordinal))
        {
            var chroma = LeadingNumber(FileNameOf(body));
            if (_tables.TryGetSkinOfChroma(chroma, out var skin))
            {
                placement = _tables.SkinTarget(_nodes, skin);
                return true;
            }

            return ChampionEntity(chroma / 1000, IconRank, "Icon", "icon", out placement);
        }

        // v1/champion-ban-vo, v1/champion-choose-vo, v1/champion-sfx-audios: champion voice clips.
        if (body.StartsWith("v1/champion-ban-vo/", StringComparison.Ordinal)
            || body.StartsWith("v1/champion-choose-vo/", StringComparison.Ordinal)
            || body.StartsWith("v1/champion-sfx-audios/", StringComparison.Ordinal))
            return ChampionEntity(LeadingNumber(FileNameOf(body)), AudioRank, "Audio", "audio", out placement);

        if (body.StartsWith("assets/characters/", StringComparison.Ordinal) || body.StartsWith("characters/", StringComparison.Ordinal))
            return TryAssignChampion(body, out placement);

        if (body.StartsWith("assets/items/", StringComparison.Ordinal)) return TryAssignItem(body, out placement);
        if (body.StartsWith("data/spells/", StringComparison.Ordinal)) return TryAssignSpell(body, out placement);

        return false;
    }

    // ── game archives ───────────────────────────────────────────────────────────────────────

    private bool TryAssignGame(string path, out CatalogPlacement placement)
    {
        placement = default;
        if (path.StartsWith("assets/sounds/", StringComparison.Ordinal)) return TryAssignSound(path, out placement);

        if (path.StartsWith("assets/characters/", StringComparison.Ordinal)
            || path.StartsWith("characters/", StringComparison.Ordinal)
            || path.StartsWith("data/characters/", StringComparison.Ordinal))
            return TryAssignChampion(path, out placement);

        if (path.StartsWith("assets/items/", StringComparison.Ordinal)
            || path.StartsWith("data/items/", StringComparison.Ordinal))
            return TryAssignItem(path, out placement);

        if (path.StartsWith("assets/perks/", StringComparison.Ordinal)
            || path.StartsWith("data/perks/", StringComparison.Ordinal))
            return TryAssignPerk(path, out placement);

        if (path.StartsWith("assets/spells/", StringComparison.Ordinal)
            || path.StartsWith("data/spells/", StringComparison.Ordinal))
            return TryAssignSpell(path, out placement);

        return false;
    }

    private bool TryAssignSound(string path, out CatalogPlacement placement)
    {
        placement = default;
        var voice = path.Contains("/vo/", StringComparison.Ordinal);
        var sfx = path.Contains("/sfx/", StringComparison.Ordinal);
        if (!voice && !sfx) return false;

        var folder = CatalogTables.ChampionFolderOf(path);
        if (folder is null) return false;

        var group = ChampionGroup(folder.ToLowerInvariant());
        if (group < 0) return false;

        var entity = voice
            ? _nodes.Entity(group, "Voice", "voice", VoiceRank)
            : _nodes.Entity(group, "Sfx", "sfx", SfxRank);
        placement = new CatalogPlacement(group, entity);
        return true;
    }

    private bool TryAssignChampion(string path, out CatalogPlacement placement)
    {
        placement = default;
        var folder = CatalogTables.ChampionFolderOf(path);
        if (folder is null) return false;

        var championFolder = folder.ToLowerInvariant();
        var group = ChampionGroup(championFolder);
        if (group < 0) return false;

        var skinFolder = CatalogTables.SkinFolderIn(path);
        int entity;
        if (!string.IsNullOrEmpty(skinFolder))
        {
            entity = ChampionSkinEntity(group, championFolder, skinFolder.ToLowerInvariant());
        }
        else if (path.Contains("/icons2d/", StringComparison.Ordinal))
        {
            entity = _nodes.Entity(group, "Icon", "icon", IconRank);
        }
        else if (path.StartsWith("data/", StringComparison.Ordinal))
        {
            entity = _nodes.Entity(group, "Data", "data", DataRank);
        }
        else
        {
            entity = _nodes.Entity(group, "Common", "common", CommonRank);
        }

        placement = new CatalogPlacement(group, entity);
        return true;
    }

    private int ChampionSkinEntity(int group, string championFolder, string skinFolder)
    {
        CatalogChampion? champion = null;
        if (_tables.TryGuessChampion(championFolder, out var found, out _)) champion = found;

        CatalogSkin? skin = null;
        if (_tables.TryGetSkinByFolder(championFolder, skinFolder, out var known))
            skin = known;
        else if (champion is not null
                 && CatalogTables.TryNumericSkinId(champion.Id, skinFolder, out var id)
                 && _tables.TryGetSkin(id, out var byId))
            skin = byId;

        // The base skin is the champion itself, so it keeps the plain "Base" label instead of the
        // champion's name ("Ahri" would read like a second champion node).
        var isBase = string.Equals(skinFolder, "base", StringComparison.OrdinalIgnoreCase);
        var label = isBase ? "Base" : skin?.Name ?? CatalogTables.SkinFolderLabel(skinFolder);
        var rank = isBase ? 0 : skin is null ? 900 : skin.Id % 1000;
        return _nodes.Entity(group, label, skinFolder, rank, skin?.Description);
    }

    private int ChampionGroup(string folder)
    {
        if (_championGroups.TryGetValue(folder, out var cached)) return cached;

        // Only the champions the client tables name become groups: a folder the tables do not know
        // ("zedshadow", "tft18_ahri", "pet…") used to show up as a guess marked "变体"; those stay
        // out of the catalog entirely.
        var group = -1;
        if (_tables.TryGuessChampion(folder, out var champion, out var guessed) && !guessed)
            group = _nodes.Group(CatalogSection.Champion, champion.Alias, CatalogTables.DetailOf(champion),
                0, folder.ToLowerInvariant());

        _championGroups[folder] = group;
        return group;
    }

    private bool ChampionEntity(int championId, int rank, string label, string folder, out CatalogPlacement placement)
    {
        placement = default;
        if (championId <= 0 || !_tables.TryGetChampionById(championId, out var champion)) return false;

        var group = _nodes.Group(CatalogSection.Champion, champion.Label, CatalogTables.DetailOf(champion), 0,
            champion.Alias.ToLowerInvariant());
        var entity = _nodes.Entity(group, label, folder, rank);
        placement = new CatalogPlacement(group, entity);
        return true;
    }

    private bool TryAssignItem(string path, out CatalogPlacement placement)
    {
        placement = default;
        var id = ItemIdOf(path);
        if (id <= 0 || !_tables.TryGetItem(id, out var item)) return false;
        placement = _tables.ItemTarget(_nodes, item);
        return true;
    }

    private static int ItemIdOf(string path)
    {
        string rest;
        if (path.StartsWith("assets/items/", StringComparison.Ordinal)) rest = path["assets/items/".Length..];
        else if (path.StartsWith("data/items/", StringComparison.Ordinal)) rest = path["data/items/".Length..];
        else return 0;

        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return 0;

        // assets/items/icons2d/<id>_name.ext, data/items/spells/<id>active.luabin64, assets/items/<id>/…
        if (segments[0] is "icons2d" or "spells") return LeadingNumber(segments[^1]);
        return LeadingNumber(segments[0]);
    }

    private bool TryAssignPerk(string path, out CatalogPlacement placement)
    {
        placement = default;

        if (path.StartsWith("assets/perks/styles/", StringComparison.Ordinal))
        {
            var rest = path["assets/perks/styles/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length == 0) return false;

            string styleToken;
            string? perkFolder = null;
            if (rest.Length == 1)
            {
                styleToken = StemStyle(rest[0]);
            }
            else
            {
                styleToken = rest[0];
                perkFolder = rest[1];
            }

            var style = _tables.TryGetPerkStyleByFolder(styleToken, out var known) ? known : null;
            var label = style?.Name ?? Title(styleToken);
            var group = _nodes.Group(CatalogSection.Perk, label, style?.Description, 0);
            var entity = perkFolder is null
                ? _nodes.Entity(group, "Icons", "icons", 1)
                : PerkEntity(group, perkFolder);
            placement = new CatalogPlacement(group, entity);
            return true;
        }

        if (path.StartsWith("assets/perks/statmods/", StringComparison.Ordinal))
        {
            var group = _nodes.Group(CatalogSection.Perk, "StatMods", "属性符文碎片", 1);
            var entity = PerkEntity(group, StemStyle(FileNameOf(path)), "StatMods");
            placement = new CatalogPlacement(group, entity);
            return true;
        }

        if (path.StartsWith("assets/perks/icons2d/", StringComparison.Ordinal))
        {
            var group = _nodes.Group(CatalogSection.Perk, "Icons", null, 1);
            var entity = PerkEntity(group, StemStyle(FileNameOf(path)), "Icons");
            placement = new CatalogPlacement(group, entity);
            return true;
        }

        return false;
    }

    private int PerkEntity(int group, string folder, string? fallback = null)
    {
        if (_tables.TryGetPerkByFolder(folder, out var perk))
            return _nodes.Entity(group, perk.Name, CatalogPaths.SafeFolderOf(perk.Name), 0, perk.Description);

        var label = fallback ?? folder;
        return _nodes.Entity(group, label, CatalogPaths.SafeFolderOf(label), 0);
    }

    private bool TryAssignSpell(string path, out CatalogPlacement placement)
    {
        placement = default;
        var id = LeadingNumber(FileNameOf(path));
        if (id <= 0 || !_tables.TryGetSpell(id, out var spell)) return false;
        placement = _tables.SpellTarget(_nodes, spell);
        return true;
    }

    // ── small helpers ───────────────────────────────────────────────────────────────────────

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

    /// <summary>"7204_resolve.tex" → "resolve", "darkharvest" → "darkharvest".</summary>
    private static string StemStyle(string token)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(token);
        var underscore = name.IndexOf('_');
        if (underscore > 0) name = name[(underscore + 1)..];
        return name;
    }

    private static string Title(string token)
        => token.Length == 0 ? token : char.ToUpperInvariant(token[0]) + token[1..];
}
