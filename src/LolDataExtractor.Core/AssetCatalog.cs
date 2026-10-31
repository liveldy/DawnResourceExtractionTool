using System.Text;

namespace LolDataExtractor.Core;

/// <summary>The buckets the asset catalog groups entries into.</summary>
public enum CatalogCategory : byte
{
    /// <summary>Nothing matched; the bucket is always shown last and never hidden.</summary>
    Unclassified = 0,
    HeroAsset = 1,
    HeroImage = 2,
    HeroAudio = 3,
    HeroConfig = 4,
    Map = 5,
    Interface = 6,
    Item = 7,
    Shared = 8,
}

/// <summary>Display and export names of one category.</summary>
public sealed record CatalogCategoryInfo(CatalogCategory Category, string Display, string Folder, string Description)
{
    /// <summary>Category keys in the order the tree shows them; unclassified is always last.</summary>
    public static IReadOnlyList<CatalogCategoryInfo> All { get; } = new[]
    {
        new CatalogCategoryInfo(CatalogCategory.HeroAsset, "英雄资源", "英雄资源", "模型、贴图、特效与动画"),
        new CatalogCategoryInfo(CatalogCategory.HeroImage, "英雄图像", "英雄图像", "原画、载入图、图标与 HUD，含相关 json"),
        new CatalogCategoryInfo(CatalogCategory.HeroAudio, "英雄音频", "英雄音频", "语音与技能音效"),
        new CatalogCategoryInfo(CatalogCategory.HeroConfig, "英雄配置", "英雄配置", "英雄数据与客户端 json"),
        new CatalogCategoryInfo(CatalogCategory.Map, "地图", "地图", "召唤师峡谷、嚎哭深渊与 TFT 棋盘"),
        new CatalogCategoryInfo(CatalogCategory.Interface, "界面与数据", "界面与数据", "客户端界面、活动与全局数据表"),
        new CatalogCategoryInfo(CatalogCategory.Item, "物品与装备", "物品与装备", "装备、消耗品与掉落图标"),
        new CatalogCategoryInfo(CatalogCategory.Shared, "共享资源", "共享资源", "多个英雄与地图共用的材质、特效与音效"),
        new CatalogCategoryInfo(CatalogCategory.Unclassified, "未分类", "未分类", "自动规则认不出来的条目"),
    };

    private static readonly Dictionary<string, CatalogCategory> ByKey = BuildKeys();

    private static Dictionary<string, CatalogCategory> BuildKeys()
    {
        var map = new Dictionary<string, CatalogCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in All)
        {
            map[info.Category.ToString()] = info.Category;
            map[info.Display] = info.Category;
            map[info.Folder] = info.Category;
        }

        return map;
    }

    public static CatalogCategoryInfo Of(CatalogCategory category)
        => All.FirstOrDefault(info => info.Category == category) ?? All[^1];

    /// <summary>Parses an override value: the enum name or the Chinese label.</summary>
    public static bool TryParse(string? text, out CatalogCategory category)
    {
        category = CatalogCategory.Unclassified;
        return !string.IsNullOrWhiteSpace(text) && ByKey.TryGetValue(text.Trim(), out category);
    }
}

/// <summary>How sure the catalog is about a hero group.</summary>
public enum CatalogHeroKind : byte
{
    /// <summary>The folder is the champion's alias exactly.</summary>
    Champion = 0,

    /// <summary>A loose rule matched, e.g. "zedshadow" or "cherry_goh_kindred".</summary>
    Guessed = 1,

    /// <summary>No champion matched; the folder name itself is the group.</summary>
    Folder = 2,
}

/// <summary>A second level node of the catalog: a champion, or a folder that is not one.</summary>
public sealed record CatalogHero(string Label, string Folder, int ChampionId, CatalogHeroKind Kind)
{
    /// <summary>True when the group is a guess or a plain folder; the tree shows a hint.</summary>
    public bool IsGuessed => Kind != CatalogHeroKind.Champion;
}

/// <summary>A third level node: one skin of a champion, or a sub folder.</summary>
public sealed record CatalogSkinLabel(string Label, string Folder, int SkinId, int ChampionId, bool IsKnown);

/// <summary>One entry of the catalog, ready to be shown, opened or exported.</summary>
public sealed record CatalogEntry(
    ulong Hash,
    string Path,
    string WadPath,
    CatalogCategory Category,
    int Hero,
    int Skin,
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

/// <summary>A contiguous run of entries: one category + hero + skin.</summary>
public sealed record CatalogGroup(int Category, int Hero, int Skin, int Start, int Count)
{
    public CatalogCategory Bucket => (CatalogCategory)Category;
}

/// <summary>Result of one search over the catalog.</summary>
public sealed record CatalogSearchResult(IReadOnlyList<CatalogEntry> Entries, int Total, bool Truncated);

/// <summary>
/// The classified view of every asset in the game and client archives: category → hero → skin →
/// file. Rows live in flat arrays (hash, name index, category, hero, skin, wad) so the whole table
/// fits in a few dozen megabytes; paths are decoded from the name index on demand.
/// </summary>
public sealed class AssetCatalog
{
    private readonly NameTable _names;

    // Row arrays, kept in hash order so a hash resolves with one binary search.
    private readonly ulong[] _hash;
    private readonly int[] _nameIndex;
    private readonly int[] _size;
    private readonly ushort[] _hero;
    private readonly ushort[] _skin;
    private readonly byte[] _category;
    private readonly ushort[] _wad;
    private readonly byte[] _wadCount;

    private readonly IReadOnlyList<string> _wadPaths;
    private readonly IReadOnlyList<CatalogHero> _heroes;
    private readonly IReadOnlyList<CatalogSkinLabel> _skins;
    private readonly IReadOnlyList<CatalogGroup> _groups;
    private readonly int[] _sorted;

    private AssetCatalog(
        NameTable names,
        ulong[] hash,
        int[] nameIndex,
        int[] size,
        ushort[] hero,
        ushort[] skin,
        byte[] category,
        ushort[] wad,
        byte[] wadCount,
        IReadOnlyList<string> wadPaths,
        IReadOnlyList<CatalogHero> heroes,
        IReadOnlyList<CatalogSkinLabel> skins,
        IReadOnlyList<CatalogGroup> groups,
        int[] sorted,
        CatalogTables tables,
        CatalogOverrides overrides)
    {
        _names = names;
        _hash = hash;
        _nameIndex = nameIndex;
        _size = size;
        _hero = hero;
        _skin = skin;
        _category = category;
        _wad = wad;
        _wadCount = wadCount;
        _wadPaths = wadPaths;
        _heroes = heroes;
        _skins = skins;
        _groups = groups;
        _sorted = sorted;
        Tables = tables;
        Overrides = overrides;
    }

    public static AssetCatalog Empty { get; } = new(
        NameTable.Empty, Array.Empty<ulong>(), Array.Empty<int>(), Array.Empty<int>(),
        Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<byte>(),
        Array.Empty<ushort>(), Array.Empty<byte>(), Array.Empty<string>(),
        Array.Empty<CatalogHero>(), Array.Empty<CatalogSkinLabel>(), Array.Empty<CatalogGroup>(),
        Array.Empty<int>(), CatalogTables.Empty, CatalogOverrides.Load(null));

    public CatalogTables Tables { get; }

    public CatalogOverrides Overrides { get; }

    public int Count => _hash.Length;

    public IReadOnlyList<CatalogHero> Heroes => _heroes;

    public IReadOnlyList<CatalogSkinLabel> Skins => _skins;

    public IReadOnlyList<CatalogGroup> Groups => _groups;

    /// <summary>Number of entries in one bucket.</summary>
    public int CountOf(CatalogCategory category)
    {
        var total = 0;
        foreach (var group in _groups)
            if (group.Category == (int)category) total += group.Count;
        return total;
    }

    public string PathOf(int row) => _nameIndex[row] >= 0 ? _names.PathAt(_nameIndex[row]) : PathHashing.ToText(_hash[row]);

    public CatalogEntry EntryOf(int row) => new(
        _hash[row],
        PathOf(row),
        _wadPaths[_wad[row]],
        (CatalogCategory)_category[row],
        _hero[row],
        _skin[row],
        _size[row],
        _wadCount[row]);

    /// <summary>Row of a hash, or -1; rows are kept in hash order.</summary>
    public int IndexOf(ulong hash) => Array.BinarySearch(_hash, hash);

    /// <summary>Entries of one group, sorted by path.</summary>
    public IReadOnlyList<CatalogEntry> EntriesOf(CatalogGroup group)
    {
        var entries = new List<CatalogEntry>(group.Count);
        for (var i = group.Start; i < group.Start + group.Count; i++) entries.Add(EntryOf(_sorted[i]));
        entries.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Entries of a bucket, optionally narrowed to one hero and one skin.</summary>
    public IReadOnlyList<CatalogEntry> EntriesOf(CatalogCategory category, int hero = -1, int skin = -1)
    {
        var entries = new List<CatalogEntry>();
        foreach (var group in _groups)
        {
            if (group.Category != (int)category) continue;
            if (hero >= 0 && group.Hero != hero) continue;
            if (skin >= 0 && group.Skin != skin) continue;
            for (var i = group.Start; i < group.Start + group.Count; i++) entries.Add(EntryOf(_sorted[i]));
        }

        entries.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Groups of a bucket in their natural order.</summary>
    public IReadOnlyList<CatalogGroup> GroupsOf(CatalogCategory category)
        => _groups.Where(group => group.Category == (int)category).ToList();

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
    /// copy whose wad language matches the path, then at the one without a language suffix.
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

        // 2. One row per hash, pointing at the copy to keep.
        var classifier = new CatalogClassifier(tables ?? CatalogTables.Empty, overrides);
        var rowHash = new ulong[total];
        var rowName = new int[total];
        var rowSize = new int[total];
        var rowWad = new ushort[total];
        var rowWadCount = new byte[total];
        var rowHero = new ushort[total];
        var rowSkin = new ushort[total];
        var rowCategory = new byte[total];
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
            var path = nameIndex >= 0 ? names.PathAt(nameIndex) : string.Empty;
            if (classifier.Classify(path, hash, out var category, out var hero, out var skin))
            {
                rowHash[rows] = hash;
                rowName[rows] = nameIndex;
                rowSize[rows] = copies[best].Size;
                rowWad[rows] = copies[best].Wad;
                rowWadCount[rows] = (byte)Math.Min(count, byte.MaxValue);
                rowHero[rows] = hero;
                rowSkin[rows] = skin;
                rowCategory[rows] = (byte)category;
                rows++;
            }

            i2 += count;
        }

        Array.Resize(ref rowHash, rows);
        Array.Resize(ref rowName, rows);
        Array.Resize(ref rowSize, rows);
        Array.Resize(ref rowWad, rows);
        Array.Resize(ref rowWadCount, rows);
        Array.Resize(ref rowHero, rows);
        Array.Resize(ref rowSkin, rows);
        Array.Resize(ref rowCategory, rows);
        log?.Report($"{rows:N0} 个条目已分类，整理分组…");

        // 3. Tree order: category, hero, skin.
        var keys = new long[rows];
        var sorted = new int[rows];
        for (var r = 0; r < rows; r++)
        {
            keys[r] = ((long)rowCategory[r] << 40) | ((long)rowHero[r] << 20) | rowSkin[r];
            sorted[r] = r;
        }

        Array.Sort(keys, sorted);

        var groups = new List<CatalogGroup>();
        var start = 0;
        for (var r = 1; r <= rows; r++)
        {
            if (r < rows && keys[r] == keys[start]) continue;
            var row = sorted[start];
            groups.Add(new CatalogGroup(rowCategory[row], rowHero[row], rowSkin[row], start, r - start));
            start = r;
        }

        log?.Report($"分组完成：{rows:N0} 个条目 / {groups.Count:N0} 个分组 / {classifier.Heroes.Count:N0} 个英雄节点");
        return new AssetCatalog(
            names, rowHash, rowName, rowSize, rowHero, rowSkin, rowCategory, rowWad, rowWadCount,
            wadPaths, classifier.Heroes, classifier.Skins, groups, sorted,
            classifier.Tables, overrides);
    }

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

/// <summary>Path helpers shared by the catalog and the classifier.</summary>
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
    /// Path of one entry inside its catalog folder: the plugin prefix and the champion/skin anchor
    /// are cut off so the tree stays readable, and whatever is left keeps its own structure.
    /// </summary>
    public static string RelativeOf(string path, string championFolder, string skinFolder)
    {
        var rest = StripPluginPrefix(path).Replace('\\', '/').TrimStart('/');

        if (championFolder.Length > 0)
        {
            var anchor = "characters/" + championFolder + "/";
            var index = rest.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                rest = rest[(index + anchor.Length)..];
                if (skinFolder.Length > 0)
                {
                    var skinAnchor = "skins/" + skinFolder + "/";
                    var skinIndex = rest.IndexOf(skinAnchor, StringComparison.OrdinalIgnoreCase);
                    if (skinIndex >= 0) rest = rest[(skinIndex + skinAnchor.Length)..];
                }
            }
        }

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

    /// <summary>
    /// Where one entry goes when the catalog exports it: <c>类别\英雄\皮肤\相对路径</c>, with the skin
    /// level left out when the entry belongs to no skin.
    /// </summary>
    public static string ExportPathOf(CatalogEntry entry, CatalogHero? hero, CatalogSkinLabel? skin)
    {
        var categoryFolder = CatalogCategoryInfo.Of(entry.Category).Folder;
        var heroName = SafeFolderOf(hero is null ? "未指派" : hero.Folder.Length > 0 ? hero.Folder : hero.Label);
        var relative = RelativeOf(entry.Path, hero?.Folder ?? string.Empty, skin?.Folder ?? string.Empty);
        if (skin is null) return Path.Combine(categoryFolder, heroName, relative);

        var skinName = SafeFolderOf(skin.Folder.Length > 0 ? skin.Folder : skin.Label);
        return skinName.Length == 0
            ? Path.Combine(categoryFolder, heroName, relative)
            : Path.Combine(categoryFolder, heroName, skinName, relative);
    }

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
