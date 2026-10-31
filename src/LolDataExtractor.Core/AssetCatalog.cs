using System.Text;

namespace LolDataExtractor.Core;

/// <summary>The sections the catalog groups entries into, in the order the tree shows them.</summary>
public enum CatalogSection : byte
{
    Champion = 0,
    Skin = 1,
    Item = 2,
    Perk = 3,
    Regalia = 4,
    Emote = 5,
    Icon = 6,
    SummonerSpell = 7,
    Ward = 8,
}

/// <summary>Display and export names of one section.</summary>
public sealed record CatalogSectionInfo(CatalogSection Section, string Display, string Folder, string Description)
{
    public static IReadOnlyList<CatalogSectionInfo> All { get; } = new[]
    {
        new CatalogSectionInfo(CatalogSection.Champion, "英雄", "英雄", "模型、贴图、动画、数据与语音"),
        new CatalogSectionInfo(CatalogSection.Skin, "皮肤", "皮肤", "原画、载入图、炫彩与系列"),
        new CatalogSectionInfo(CatalogSection.Item, "装备", "装备", "图标、属性与脚本数据"),
        new CatalogSectionInfo(CatalogSection.Perk, "符文", "符文", "符文与符文系图标"),
        new CatalogSectionInfo(CatalogSection.Regalia, "旗帜", "旗帜", "横幅与旗帜"),
        new CatalogSectionInfo(CatalogSection.Emote, "表情", "表情", "表情图标与套组"),
        new CatalogSectionInfo(CatalogSection.Icon, "图标", "图标", "召唤师图标与套组"),
        new CatalogSectionInfo(CatalogSection.SummonerSpell, "召唤师技能", "召唤师技能", "技能图标与数据"),
        new CatalogSectionInfo(CatalogSection.Ward, "守卫", "守卫", "守卫皮肤图标"),
    };

    private static readonly Dictionary<string, CatalogSection> ByKey = BuildKeys();

    private static Dictionary<string, CatalogSection> BuildKeys()
    {
        var map = new Dictionary<string, CatalogSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in All)
        {
            map[info.Section.ToString()] = info.Section;
            map[info.Display] = info.Section;
            map[info.Folder] = info.Section;
        }

        return map;
    }

    public static CatalogSectionInfo Of(CatalogSection section)
        => All.FirstOrDefault(info => info.Section == section) ?? All[0];

    /// <summary>Parses an override value: the enum name or the Chinese label.</summary>
    public static bool TryParse(string? text, out CatalogSection section)
    {
        section = CatalogSection.Champion;
        return !string.IsNullOrWhiteSpace(text) && ByKey.TryGetValue(text.Trim(), out section);
    }
}

/// <summary>A second level node: a champion, a skin series, an item category, an icon set, …</summary>
public sealed class CatalogGroup
{
    internal CatalogGroup(CatalogSection section, string label, string folder, string? detail, byte rank)
    {
        Section = section;
        Label = label;
        Folder = folder;
        Detail = detail;
        Rank = rank;
    }

    public CatalogSection Section { get; }

    public string Label { get; }

    /// <summary>Folder name used when this group is exported.</summary>
    public string Folder { get; }

    /// <summary>Extra text shown as a tool tip: the champion description, the series description, …</summary>
    public string? Detail { get; }

    /// <summary>0 for what the client tables name, 1 for the rest (variants, "Other").</summary>
    public byte Rank { get; }

    public int Start { get; internal set; }

    public int Count { get; internal set; }
}

/// <summary>A third level node: one skin, one item, one perk, one icon, …</summary>
public sealed class CatalogEntity
{
    internal CatalogEntity(int group, string label, string folder, string? detail, int rank)
    {
        Group = group;
        Label = label;
        Folder = folder;
        Detail = detail;
        Rank = rank;
    }

    public int Group { get; }

    public string Label { get; }

    /// <summary>Folder name used when this entity is exported.</summary>
    public string Folder { get; }

    public string? Detail { get; }

    /// <summary>Sort key inside its group: the skin number, a fixed slot for the special nodes.</summary>
    public int Rank { get; }

    /// <summary>
    /// Longest folder prefix every file of this entity shares; an entry's path is shown and exported
    /// after it. Computed once when the catalog is built.
    /// </summary>
    public string Anchor { get; internal set; } = string.Empty;

    public int Start { get; internal set; }

    public int Count { get; internal set; }
}

/// <summary>One entry of the catalog, ready to be shown, opened or exported.</summary>
public sealed record CatalogEntry(
    ulong Hash,
    string Path,
    string WadPath,
    CatalogSection Section,
    int Group,
    int Entity,
    long Size,
    int WadCount)
{
    public string FileName
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path[(slash + 1)..];
        }
    }

    public string Extension
    {
        get
        {
            var name = FileName;
            var dot = name.LastIndexOf('.');
            return dot <= 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
        }
    }
}

/// <summary>Result of one search over the catalog.</summary>
public sealed record CatalogSearchResult(IReadOnlyList<CatalogEntry> Entries, int Total, bool Truncated);

/// <summary>Where one entry sits in the tree: a group and an entity of <see cref="CatalogNodes"/>.</summary>
internal readonly record struct CatalogPlacement(int Group, int Entity);

/// <summary>
/// The classified view of every asset in the game and client archives: section → group → entity →
/// file. Rows live in flat arrays (hash, name index, group, entity, wad) so the whole table fits in
/// a few dozen megabytes; paths are decoded from the name index on demand.
/// </summary>
public sealed class AssetCatalog
{
    private readonly NameTable _names;

    // Row arrays, kept in hash order so a hash resolves with one binary search.
    private readonly ulong[] _hash;
    private readonly int[] _nameIndex;
    private readonly int[] _size;
    private readonly ushort[] _group;
    private readonly ushort[] _entity;
    private readonly ushort[] _wad;
    private readonly byte[] _wadCount;

    private readonly IReadOnlyList<string> _wadPaths;
    private readonly IReadOnlyList<CatalogGroup> _groups;
    private readonly IReadOnlyList<CatalogEntity> _entities;
    private readonly int[] _sorted;

    private AssetCatalog(
        NameTable names,
        ulong[] hash,
        int[] nameIndex,
        int[] size,
        ushort[] group,
        ushort[] entity,
        ushort[] wad,
        byte[] wadCount,
        IReadOnlyList<string> wadPaths,
        IReadOnlyList<CatalogGroup> groups,
        IReadOnlyList<CatalogEntity> entities,
        int[] sorted,
        CatalogTables tables,
        CatalogOverrides overrides)
    {
        _names = names;
        _hash = hash;
        _nameIndex = nameIndex;
        _size = size;
        _group = group;
        _entity = entity;
        _wad = wad;
        _wadCount = wadCount;
        _wadPaths = wadPaths;
        _groups = groups;
        _entities = entities;
        _sorted = sorted;
        Tables = tables;
        Overrides = overrides;
    }

    public static AssetCatalog Empty { get; } = new(
        NameTable.Empty, Array.Empty<ulong>(), Array.Empty<int>(), Array.Empty<int>(),
        Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<byte>(),
        Array.Empty<string>(), Array.Empty<CatalogGroup>(), Array.Empty<CatalogEntity>(),
        Array.Empty<int>(), CatalogTables.Empty, CatalogOverrides.Load(null));

    public CatalogTables Tables { get; }

    public CatalogOverrides Overrides { get; }

    public int Count => _hash.Length;

    public IReadOnlyList<CatalogGroup> Groups => _groups;

    public IReadOnlyList<CatalogEntity> Entities => _entities;

    /// <summary>Number of entries in one section.</summary>
    public int CountOf(CatalogSection section)
    {
        var total = 0;
        foreach (var group in _groups)
            if (group.Section == section) total += group.Count;
        return total;
    }

    public string PathOf(int row) => _nameIndex[row] >= 0 ? _names.PathAt(_nameIndex[row]) : PathHashing.ToText(_hash[row]);

    public CatalogEntry EntryOf(int row) => new(
        _hash[row],
        PathOf(row),
        _wadPaths[_wad[row]],
        _groups[_group[row]].Section,
        _group[row],
        _entity[row],
        _size[row],
        _wadCount[row]);

    /// <summary>Row of a hash, or -1; rows are kept in hash order.</summary>
    public int IndexOf(ulong hash) => Array.BinarySearch(_hash, hash);

    /// <summary>
    /// Entries of one group, or of one entity when <paramref name="entity"/> is given, sorted by path.
    /// </summary>
    public IReadOnlyList<CatalogEntry> EntriesOf(int group, int entity = -1)
    {
        if (group < 0 || group >= _groups.Count) return Array.Empty<CatalogEntry>();

        int start;
        int count;
        if (entity >= 0)
        {
            if (entity >= _entities.Count) return Array.Empty<CatalogEntry>();
            var item = _entities[entity];
            if (item.Group != group) return Array.Empty<CatalogEntry>();
            start = item.Start;
            count = item.Count;
        }
        else
        {
            var item = _groups[group];
            start = item.Start;
            count = item.Count;
        }

        if (count == 0) return Array.Empty<CatalogEntry>();
        var entries = new List<CatalogEntry>(count);
        for (var i = start; i < start + count; i++) entries.Add(EntryOf(_sorted[i]));
        entries.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Entries of a whole section.</summary>
    public IReadOnlyList<CatalogEntry> EntriesOf(CatalogSection section)
    {
        var entries = new List<CatalogEntry>();
        foreach (var group in _groups)
        {
            if (group.Section != section) continue;
            for (var i = group.Start; i < group.Start + group.Count; i++) entries.Add(EntryOf(_sorted[i]));
        }

        entries.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Groups of a section that actually hold files.</summary>
    public IReadOnlyList<CatalogGroup> GroupsOf(CatalogSection section)
        => _groups.Where(group => group.Section == section && group.Count > 0).ToList();

    /// <summary>Entities of a group that actually hold files.</summary>
    public IReadOnlyList<CatalogEntity> EntitiesOf(int group)
        => _entities.Where(entity => entity.Group == group && entity.Count > 0).ToList();

    /// <summary>Substring search over every path; <paramref name="limit"/> caps the rows returned.</summary>
    public CatalogSearchResult Search(string text, int limit = 20000)
    {
        var needle = (text ?? string.Empty).Trim();
        if (needle.Length == 0) return new CatalogSearchResult(Array.Empty<CatalogEntry>(), 0, false);

        // Non-ASCII text cannot be matched byte-wise in the blob; fall back to decoding per row.
        return IsAscii(needle) ? SearchBlob(needle, limit) : SearchDecoded(needle, limit);
    }

    private static bool IsAscii(string text)
    {
        foreach (var c in text)
            if (c > 127) return false;
        return true;
    }

    private CatalogSearchResult SearchBlob(string needle, int limit)
    {
        var results = new List<CatalogEntry>();
        var total = 0;
        var span = _names.Blob;
        var at = 0;
        while (at <= span.Length - needle.Length)
        {
            var found = FindBytes(span, at, needle);
            if (found < 0) break;

            var index = _names.FindIndexByOffset(found);
            if (index < 0)
            {
                at = found + 1;
                continue;
            }

            // One match per path: continue at the entry that follows this one.
            var next = index + 1 < _names.Count ? _names.PathByteOffsetAt(index + 1) : span.Length;
            at = Math.Max(next, found + 1);

            var row = IndexOf(_names.HashAt(index));
            if (row < 0) continue;

            total++;
            if (results.Count < limit) results.Add(EntryOf(row));
        }

        return new CatalogSearchResult(results, total, total > results.Count);
    }

    private CatalogSearchResult SearchDecoded(string needle, int limit)
    {
        var results = new List<CatalogEntry>();
        var total = 0;
        for (var row = 0; row < _hash.Length; row++)
        {
            var path = PathOf(row);
            if (path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            total++;
            if (results.Count < limit) results.Add(EntryOf(row));
        }

        return new CatalogSearchResult(results, total, total > results.Count);
    }

    /// <summary>Case-insensitive byte search over the name blob (asset paths are ASCII).</summary>
    private static int FindBytes(ReadOnlySpan<byte> haystack, int start, string needle)
    {
        var last = haystack.Length - needle.Length;
        for (var i = start; i <= last; i++)
        {
            var matched = true;
            for (var k = 0; k < needle.Length; k++)
            {
                var a = haystack[i + k];
                var b = (byte)needle[k];
                if (a != b)
                {
                    if (a is >= (byte)'A' and <= (byte)'Z') a = (byte)(a + 32);
                    if (b is >= (byte)'A' and <= (byte)'Z') b = (byte)(b + 32);
                    if (a != b)
                    {
                        matched = false;
                        break;
                    }
                }
            }

            if (matched) return i;
        }

        return -1;
    }

    // ── build ────────────────────────────────────────────────────────────────────────────────

    private struct CopyKey : IComparable<CopyKey>
    {
        public ulong Hash;
        public ushort Wad;
        public int Size;

        public int CompareTo(CopyKey other) => Hash.CompareTo(other.Hash);
    }

    /// <summary>
    /// Scans every wad, resolves each entry's path through <paramref name="names"/>, classifies it and
    /// returns the catalog. An entry that appears in more than one wad is kept once, pointing at the
    /// copy whose wad language matches the path, then at the one without a language suffix. Entries
    /// no section claims are left out: the tree has no "unclassified" bucket.
    /// </summary>
    public static AssetCatalog Build(
        IReadOnlyList<WadSummary> wads,
        NameTable names,
        CatalogOverrides? overrides = null,
        CatalogTables? tables = null,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wads);
        ArgumentNullException.ThrowIfNull(names);

        overrides ??= CatalogOverrides.Load(null);
        tables ??= CatalogTables.Empty;
        var wadPaths = wads.Select(wad => wad.FullPath).ToArray();

        // 1. Every (hash, wad, size) triple.
        var capacity = Math.Max(16, wads.Sum(wad => wad.EntryCount));
        var copies = new CopyKey[capacity];
        var total = 0;

        for (var i = 0; i < wads.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wad = wads[i];
            try
            {
                using var archive = WadArchive.Open(wad.FullPath);
                foreach (var entry in archive.ReadEntries(names: null))
                {
                    if (total == copies.Length) Array.Resize(ref copies, copies.Length * 2);
                    copies[total++] = new CopyKey
                    {
                        Hash = entry.Hash,
                        Wad = (ushort)i,
                        Size = (int)Math.Min(entry.UncompressedSize, int.MaxValue),
                    };
                }
            }
            catch (Exception ex)
            {
                log?.Report($"{wad.Name}: 无法读取（{ex.Message}）");
            }
        }

        Array.Sort(copies, 0, total);
        log?.Report($"{total:N0} 个 wad 条目，去重中…");

        // 2. The tables say which file belongs where; the path rules fill the gaps.
        var nodes = new CatalogNodes();
        var classifier = new CatalogClassifier(tables, overrides, nodes);
        var references = tables.BuildReferenceIndex(nodes, log);

        var rowHash = new ulong[total];
        var rowName = new int[total];
        var rowSize = new int[total];
        var rowWad = new ushort[total];
        var rowWadCount = new byte[total];
        var rowGroup = new ushort[total];
        var rowEntity = new ushort[total];
        var rows = 0;

        var i2 = 0;
        while (i2 < total)
        {
            if ((i2 & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var hash = copies[i2].Hash;
            var best = i2;
            var count = 1;
            while (i2 + count < total && copies[i2 + count].Hash == hash)
            {
                if (IsBetterCopy(names, hash, wadPaths, copies[i2 + count].Wad, copies[best].Wad)) best = i2 + count;
                count++;
            }

            var nameIndex = names.IndexOf(hash);
            if (nameIndex >= 0)
            {
                var path = names.PathAt(nameIndex);
                var placement = default(CatalogPlacement);
                var assigned = references.TryGetValue(hash, out placement) || classifier.TryAssign(path, out placement);
                if (assigned && classifier.Apply(hash, path, ref placement))
                {
                    rowHash[rows] = hash;
                    rowName[rows] = nameIndex;
                    rowSize[rows] = copies[best].Size;
                    rowWad[rows] = copies[best].Wad;
                    rowWadCount[rows] = (byte)Math.Min(count, byte.MaxValue);
                    rowGroup[rows] = (ushort)placement.Group;
                    rowEntity[rows] = (ushort)placement.Entity;
                    rows++;
                }
            }

            i2 += count;
        }

        Array.Resize(ref rowHash, rows);
        Array.Resize(ref rowName, rows);
        Array.Resize(ref rowSize, rows);
        Array.Resize(ref rowWad, rows);
        Array.Resize(ref rowWadCount, rows);
        Array.Resize(ref rowGroup, rows);
        Array.Resize(ref rowEntity, rows);
        log?.Report($"{rows:N0} 个条目属于分类，整理分组…");

        // 3. Tree order: group, then entity.
        var keys = new long[rows];
        var sorted = new int[rows];
        for (var r = 0; r < rows; r++)
        {
            keys[r] = ((long)rowGroup[r] << 16) | rowEntity[r];
            sorted[r] = r;
        }

        Array.Sort(keys, sorted);

        // 4. Row ranges per group and entity, and the folder prefix each entity's files share.
        var groupStart = new int[nodes.Groups.Count];
        var groupCount = new int[nodes.Groups.Count];
        var entityStart = new int[nodes.Entities.Count];
        var entityCount = new int[nodes.Entities.Count];
        var entityAnchor = new string[nodes.Entities.Count];

        var i3 = 0;
        while (i3 < rows)
        {
            var group = rowGroup[sorted[i3]];
            var first = i3;
            while (i3 < rows && rowGroup[sorted[i3]] == group)
            {
                var entity = rowEntity[sorted[i3]];
                var start = i3;
                while (i3 < rows && rowGroup[sorted[i3]] == group && rowEntity[sorted[i3]] == entity) i3++;

                entityStart[entity] = start;
                entityCount[entity] = i3 - start;
                entityAnchor[entity] = CommonAnchor(names, rowName, sorted, start, i3);
            }

            groupStart[group] = first;
            groupCount[group] = i3 - first;
        }

        for (var g = 0; g < groupStart.Length; g++)
        {
            nodes.Groups[g].Start = groupStart[g];
            nodes.Groups[g].Count = groupCount[g];
        }

        for (var e = 0; e < entityStart.Length; e++)
        {
            nodes.Entities[e].Start = entityStart[e];
            nodes.Entities[e].Count = entityCount[e];
            nodes.Entities[e].Anchor = entityAnchor[e] ?? string.Empty;
        }

        log?.Report($"分组完成：{rows:N0} 个条目 / {nodes.Groups.Count:N0} 个组 / {nodes.Entities.Count:N0} 个条目节点");
        return new AssetCatalog(
            names, rowHash, rowName, rowSize, rowGroup, rowEntity, rowWad, rowWadCount,
            wadPaths, nodes.Groups, nodes.Entities, sorted,
            tables, overrides);
    }

    /// <summary>
    /// Longest folder prefix shared by a run of rows, ending with a slash; empty when none. Rows are
    /// positions in <paramref name="sorted"/>, so a path is decoded through
    /// <paramref name="nameIndex"/> — the row itself is not a name index.
    /// </summary>
    private static string CommonAnchor(NameTable names, int[] nameIndex, int[] sorted, int start, int end)
    {
        if (start >= end) return string.Empty;

        var first = RowPath(names, nameIndex, sorted[start]);
        if (first.Length == 0) return string.Empty;
        var slash = first.LastIndexOf('/');
        if (slash <= 0) return string.Empty;
        var anchor = first[..(slash + 1)];

        for (var i = start + 1; i < end && anchor.Length > 0; i++)
        {
            var other = RowPath(names, nameIndex, sorted[i]);
            var length = 0;
            var limit = Math.Min(anchor.Length, other.Length);
            while (length < limit && char.ToLowerInvariant(anchor[length]) == char.ToLowerInvariant(other[length])) length++;
            if (length < anchor.Length)
            {
                // Cut back to the last folder boundary before the difference.
                var boundary = anchor.LastIndexOf('/', Math.Max(0, length - 1));
                anchor = boundary < 0 ? string.Empty : anchor[..(boundary + 1)];
            }
        }

        return anchor.Length > 1 ? anchor : string.Empty;
    }

    /// <summary>Path of one row, without the client plugin prefix and with forward slashes.</summary>
    private static string RowPath(NameTable names, int[] nameIndex, int row)
        => nameIndex[row] >= 0
            ? CatalogPaths.StripPluginPrefix(names.PathAt(nameIndex[row])).Replace('\\', '/')
            : string.Empty;

    /// <summary>
    /// True when the candidate copy should replace the current one: the wad whose language matches
    /// the path's language wins, then the wad without a language suffix, then the lexical order.
    /// </summary>
    private static bool IsBetterCopy(NameTable names, ulong hash, IReadOnlyList<string> wadPaths, int candidate, int current)
    {
        if (candidate == current) return false;

        var language = CatalogPaths.LanguageOf(names.GetPath(hash) ?? string.Empty);
        var candidateRank = CopyRank(wadPaths[candidate], language);
        var currentRank = CopyRank(wadPaths[current], language);
        if (candidateRank != currentRank) return candidateRank < currentRank;

        return string.Compare(wadPaths[candidate], wadPaths[current], StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int CopyRank(string wadPath, string language)
    {
        var match = CatalogPaths.LocaleSuffix.Match(Path.GetFileName(wadPath));
        if (!match.Success) return language.Length == 0 ? 0 : 1;

        if (language.Length == 0) return 1;
        return string.Equals(match.Groups[1].Value, language, StringComparison.OrdinalIgnoreCase) ? 0 : 2;
    }
}

/// <summary>Path helpers shared by the catalog, the tables and the classifier.</summary>
public static class CatalogPaths
{
    /// <summary>Client plugin path prefix, dropped when comparing or exporting.</summary>
    private static readonly System.Text.RegularExpressions.Regex PluginPrefix =
        new(@"^plugins/[^/]+/[^/]+/[^/]+/", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"Ahri.en_US.wad.client" → en_US; "Ahri.wad.client" → no match.</summary>
    public static readonly System.Text.RegularExpressions.Regex LocaleSuffix =
        new(@"\.([a-z]{2}_[A-Z]{2})\.wad", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Language segment of a voice path, e.g. <c>en_us</c> for <c>…/vo/en_us/characters/…</c>.</summary>
    public static string LanguageOf(string path)
    {
        const string marker = "/vo/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;
        var start = index + marker.Length;
        var end = path.IndexOf('/', start);
        if (end < 0) return string.Empty;
        var language = path[start..end];
        return string.Equals(language, "characters", StringComparison.OrdinalIgnoreCase) ? string.Empty : language;
    }

    /// <summary>Drops the client plugin prefix so both trees classify on the same shapes.</summary>
    public static string StripPluginPrefix(string path)
    {
        var match = PluginPrefix.Match(path);
        return match.Success ? path[match.Length..] : path;
    }

    /// <summary>
    /// Path of one entry below its entity: the plugin prefix and the folder prefix every file of the
    /// entity shares are cut off, and whatever is left keeps its own structure.
    /// </summary>
    public static string RelativeOf(string path, string anchor)
    {
        var rest = StripPluginPrefix(path).Replace('\\', '/').TrimStart('/');
        if (anchor.Length > 0 && rest.StartsWith(anchor, StringComparison.OrdinalIgnoreCase))
            rest = rest[anchor.Length..];
        return SanitizePath(rest);
    }

    /// <summary>
    /// Archive paths a <c>/lol-game-data/…</c> value of the client json can point at: both locale
    /// folders, and the three shapes the paths are written in (as is, without <c>assets/</c>, or
    /// with the doubled <c>assets/assets/</c> prefix trimmed).
    /// </summary>
    public static IReadOnlyList<string> ClientCandidates(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return Array.Empty<string>();

        var rest = reference.Trim().Trim('"');
        const string marker = "/lol-game-data/";
        var index = rest.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) rest = rest[(index + marker.Length)..];
        rest = rest.TrimStart('/');
        if (rest.Length == 0) return Array.Empty<string>();

        var forms = new List<string> { rest };
        if (rest.StartsWith("assets/assets/", StringComparison.OrdinalIgnoreCase))
            forms.Add("assets/" + rest["assets/assets/".Length..]);
        else if (rest.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            forms.Add(rest["assets/".Length..]);

        var candidates = new List<string>(forms.Count * 2);
        foreach (var locale in new[] { "default", "zh_cn" })
        foreach (var form in forms)
            candidates.Add($"plugins/rcp-be-lol-game-data/global/{locale}/{form}");

        return candidates;
    }

    /// <summary>True when a reference names a file (some emotes point at a bare folder).</summary>
    public static bool LooksLikeFile(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var value = reference.Trim();
        var slash = value.LastIndexOf('/');
        var name = slash < 0 ? value : value[(slash + 1)..];
        return name.Contains('.') && !name.EndsWith(".", StringComparison.Ordinal);
    }

    /// <summary>
    /// Where one entry goes when the catalog exports it:
    /// <c>类别\组\条目\相对路径</c>.
    /// </summary>
    public static string ExportPathOf(CatalogEntry entry, CatalogSectionInfo section, CatalogGroup group, CatalogEntity entity)
    {
        var relative = RelativeOf(entry.Path, entity.Anchor);
        if (relative.Length == 0) relative = SafeFolderOf(entry.FileName);
        return Path.Combine(EntityFolderOf(section, group, entity), relative);
    }

    /// <summary>The folder of one entity below the export root: <c>类别\组\条目</c>.</summary>
    public static string EntityFolderOf(CatalogSectionInfo section, CatalogGroup group, CatalogEntity entity)
        => Path.Combine(section.Folder, SafeFolderOf(group.Folder), SafeFolderOf(entity.Folder));

    /// <summary>Turns a label into a folder name Windows accepts.</summary>
    public static string SafeFolderOf(string label)
    {
        var cleaned = SanitizePath(label.Trim().Replace(' ', '_'));
        return cleaned.Length == 0 ? "unnamed" : cleaned;
    }

    /// <summary>Replaces characters Windows refuses in a file name, keeping the folder structure.</summary>
    public static string SanitizePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] is "." or "..")
            {
                segments[i] = "_";
                continue;
            }

            if (segments[i].IndexOfAny(invalid) < 0) continue;
            var builder = new StringBuilder(segments[i].Length);
            foreach (var c in segments[i]) builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            segments[i] = builder.ToString();
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }
}
