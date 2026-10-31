using System.Text.Json;

namespace LolDataExtractor.Core;

/// <summary>One champion of the client's <c>champion-summary.json</c>.</summary>
public sealed record CatalogChampion(int Id, string Alias)
{
    /// <summary>
    /// Label used in the tree: the English alias, which is also the folder name the assets live in.
    /// The localized names are not used — "Jade_Ahri" and "Ahri" are different champions here, and
    /// their display names are both "Ahri" in either language.
    /// </summary>
    public string Label => Alias;
}

/// <summary>One skin of the client's <c>skins.json</c>.</summary>
public sealed record CatalogSkin(int Id, int ChampionId, string Name, bool IsBase, bool IsLegacy);

/// <summary>
/// Champion and skin tables read from the client plugin archives. They are the authority the
/// catalog classifies against; everything derived from a folder name alone is a guess.
/// </summary>
public sealed class CatalogTables
{
    private const string CharactersMarker = "characters/";
    private const string SkinsMarker = "/skins/";

    private readonly Dictionary<string, CatalogChampion> _byAlias;
    private readonly Dictionary<int, CatalogChampion> _byId;
    private readonly Dictionary<int, CatalogSkin> _skinsById;

    /// <summary>"ahri|skin05" → skin id, built from the image paths of every skin.</summary>
    private readonly Dictionary<string, int> _skinByFolder;

    /// <summary>Aliases longest first, so "lee sin" wins over "lee" when guessing.</summary>
    private readonly string[] _aliasesByLength;

    private CatalogTables(
        IReadOnlyList<CatalogChampion> champions,
        IReadOnlyList<CatalogSkin> skins,
        Dictionary<string, CatalogChampion> byAlias,
        Dictionary<string, int> skinByFolder)
    {
        Champions = champions;
        Skins = skins;
        _byAlias = byAlias;
        _skinsById = skins.ToDictionary(skin => skin.Id);
        _byId = champions.ToDictionary(champion => champion.Id);
        _skinByFolder = skinByFolder;
        _aliasesByLength = byAlias.Keys.OrderByDescending(alias => alias.Length).ToArray();
    }

    public static CatalogTables Empty { get; } = new(
        Array.Empty<CatalogChampion>(),
        Array.Empty<CatalogSkin>(),
        new Dictionary<string, CatalogChampion>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<CatalogChampion> Champions { get; }

    public IReadOnlyList<CatalogSkin> Skins { get; }

    public bool IsEmpty => Champions.Count == 0;

    /// <summary>
    /// Reads both tables from the raw json of the client archives. The champion <c>alias</c> is the
    /// only field taken from <paramref name="summaryJson"/>; the skin <c>name</c> of
    /// <paramref name="skinsJson"/> is shown as written, so the English copy gives English skin names.
    /// </summary>
    public static CatalogTables FromJson(string? summaryJson, string? skinsJson)
    {
        var champions = new List<CatalogChampion>();
        if (!string.IsNullOrWhiteSpace(summaryJson))
        {
            try
            {
                using var document = JsonDocument.Parse(summaryJson);
                foreach (var item in EnumerateObjects(document.RootElement))
                {
                    var id = ReadInt(item, "id");
                    var alias = ReadString(item, "alias");
                    if (id <= 0 || string.IsNullOrWhiteSpace(alias)) continue;
                    champions.Add(new CatalogChampion(id, alias));
                }
            }
            catch (JsonException)
            {
                // A truncated table only costs labels; the catalog still classifies by folder name.
            }
        }

        var skins = new List<CatalogSkin>();
        var skinByFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(skinsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(skinsJson);
                foreach (var item in EnumerateObjects(document.RootElement))
                {
                    var id = ReadInt(item, "id");
                    if (id <= 0) continue;
                    var skin = new CatalogSkin(
                        id,
                        id / 1000,
                        ReadString(item, "name") ?? id.ToString(),
                        ReadBool(item, "isBase"),
                        ReadBool(item, "isLegacy"));
                    skins.Add(skin);

                    foreach (var field in SkinPathFields)
                    {
                        if (SkinFolderOf(ReadString(item, field)) is { } folder)
                            skinByFolder[folder] = id;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        var byAlias = new Dictionary<string, CatalogChampion>(StringComparer.OrdinalIgnoreCase);
        foreach (var champion in champions) byAlias[champion.Alias] = champion;

        return new CatalogTables(champions, skins, byAlias, skinByFolder);
    }

    /// <summary>
    /// Reads the two tables out of the client plugin archives. The English (<c>default</c>) copy is
    /// preferred — champion labels are aliases and skin names are shown in English — and the
    /// localized one is the fallback, so a client install that carries only one of them still works.
    /// </summary>
    public static CatalogTables LoadFromClient(
        IReadOnlyList<WadSummary> clientWads,
        NameTable clientNames,
        IProgress<string>? log = null)
    {
        string? summaryLocalized = null;
        string? summaryDefault = null;
        string? skinsLocalized = null;
        string? skinsDefault = null;

        foreach (var wad in clientWads)
        {
            try
            {
                using var archive = WadArchive.Open(wad.FullPath);
                foreach (var entry in archive.ReadEntries(clientNames))
                {
                    if (entry.Path is not { } path) continue;
                    var lowered = path.ToLowerInvariant();

                    var isSummary = lowered.EndsWith("/v1/champion-summary.json", StringComparison.Ordinal);
                    var isSkins = lowered.EndsWith("/v1/skins.json", StringComparison.Ordinal);
                    if (!isSummary && !isSkins) continue;

                    var localized = lowered.Contains("/zh_cn/", StringComparison.Ordinal);
                    var english = lowered.Contains("/default/", StringComparison.Ordinal);
                    if (!localized && !english) continue;
                    if (localized && isSummary && summaryLocalized is not null) continue;
                    if (localized && isSkins && skinsLocalized is not null) continue;
                    if (english && isSummary && summaryDefault is not null) continue;
                    if (english && isSkins && skinsDefault is not null) continue;

                    var bytes = ReadAll(archive, entry.Hash);
                    if (bytes is null) continue;
                    var json = System.Text.Encoding.UTF8.GetString(bytes);

                    if (isSummary)
                    {
                        if (localized) summaryLocalized = json;
                        else summaryDefault = json;
                    }
                    else
                    {
                        if (localized) skinsLocalized = json;
                        else skinsDefault = json;
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Report($"{wad.Name}: 读取英雄表失败（{ex.Message}）");
            }
        }

        // The English copies win: the tree shows aliases and skin names as the game writes them in
        // English, and a client that only ships the localized copy still classifies.
        var summary = summaryDefault ?? summaryLocalized;
        if (summary is null)
        {
            log?.Report("客户端包里没有 champion-summary.json，英雄节点只能按目录名推测");
            return Empty;
        }

        var tables = FromJson(summary, skinsDefault ?? skinsLocalized);
        log?.Report($"英雄表：{tables.Champions.Count} 个英雄，{tables.Skins.Count} 个皮肤");
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

    /// <summary>Json fields of a skin that point at a folder below <c>characters/…/skins/</c>.</summary>
    private static readonly string[] SkinPathFields =
    {
        "splashPath",
        "uncenteredSplashPath",
        "tilePath",
        "loadScreenPath",
        "splashVideoPath",
        "collectionSplashVideoPath",
        "collectionCardHoverVideoPath",
        "chromaPath",
    };

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object) yield return item;
                break;

            case JsonValueKind.Object:
                // skins.json is an object keyed by skin id.
                foreach (var property in root.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.Object) yield return property.Value;
                break;
        }
    }

    private static int ReadInt(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static bool ReadBool(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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

    /// <summary>Looks a champion up by alias, ignoring case.</summary>
    public bool TryGetChampionByAlias(string? alias, out CatalogChampion champion)
    {
        champion = null!;
        return !string.IsNullOrWhiteSpace(alias) && _byAlias.TryGetValue(alias.Trim(), out champion!);
    }

    /// <summary>Looks a champion up by its numeric id.</summary>
    public bool TryGetChampionById(int id, out CatalogChampion champion)
        => _byId.TryGetValue(id, out champion!);

    public bool TryGetSkin(int id, out CatalogSkin skin) => _skinsById.TryGetValue(id, out skin!);

    /// <summary>Resolves a skin from the folder pair found on an asset path.</summary>
    public bool TryGetSkinByFolder(string championFolder, string skinFolder, out CatalogSkin skin)
    {
        skin = null!;
        return !string.IsNullOrWhiteSpace(championFolder)
               && !string.IsNullOrWhiteSpace(skinFolder)
               && _skinByFolder.TryGetValue(championFolder + "|" + skinFolder, out var id)
               && _skinsById.TryGetValue(id, out skin!);
    }

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
}
