using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DawnResourceExtractionTool.Core;

/// <summary>One client table transcribed into an export: the file written and how many records it kept.</summary>
public sealed record CatalogJsonFile(string Table, string Path, int Records, int Total);

/// <summary>
/// Transcribes the client tables that document an export into its root.
/// <para>
/// Not a copy: only the records an exported file refers to stay (a skin export keeps its own skin
/// record, the series it belongs to and the universe owning that series), fields with nothing in
/// them are dropped, and every asset path is rewritten to where the file landed in the export.
/// </para>
/// </summary>
public static class CatalogJsonExport
{
    /// <summary>The tables that document one section, in the order they are written.</summary>
    public static IReadOnlyList<string> TablesOf(CatalogSection section) => section switch
    {
        CatalogSection.Champion => new[] { "champion-summary.json" },
        CatalogSection.Skin => new[] { "skins.json", "skinlines.json", "universes.json", "skinborders.json" },
        CatalogSection.Item => new[] { "items.json" },
        CatalogSection.Perk => new[] { "perks.json", "perkstyles.json" },
        CatalogSection.Regalia => new[] { "regalia.json" },
        CatalogSection.Emote => new[] { "summoner-emotes.json" },
        CatalogSection.Icon => new[] { "summoner-icons.json", "summoner-icon-sets.json" },
        CatalogSection.SummonerSpell => new[] { "summoner-spells.json" },
        CatalogSection.Ward => new[] { "ward-skins.json", "ward-skin-sets.json" },
        _ => Array.Empty<string>(),
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes the section's tables into <paramref name="root"/>; <paramref name="destinations"/> maps the
    /// hash of an exported entry to its path relative to that root.
    /// </summary>
    public static IReadOnlyList<CatalogJsonFile> Write(
        string root,
        CatalogTables tables,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyDictionary<ulong, string> destinations)
    {
        if (entries.Count == 0 || tables.IsEmpty) return Array.Empty<CatalogJsonFile>();

        var section = entries[0].Section;
        var assets = new ExportPaths(entries, destinations);
        var written = new List<CatalogJsonFile>();

        switch (section)
        {
            case CatalogSection.Champion:
            {
                // 只留这次导出的英雄；表里 id 为 -1 的「无」这种占位记录本来就不在导出里。
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                    if (CatalogTables.ChampionFolderOf(entry.Path) is { Length: > 0 } alias) aliases.Add(alias);
                WriteTable(root, tables, "champion-summary.json", assets,
                    record => record["alias"] is JsonValue alias
                              && alias.TryGetValue<string>(out var text) && aliases.Contains(text), written);
                break;
            }

            case CatalogSection.Skin:
            {
                var skins = WriteTable(root, tables, "skins.json", assets, null, written);
                var lines = IdsIn(skins, "skinLines");
                WriteTable(root, tables, "skinlines.json", assets,
                    record => IdOf(record) is var id && lines.Contains(id), written);
                WriteTable(root, tables, "universes.json", assets,
                    record => record["skinSets"] is JsonArray sets && sets.Any(node => lines.Contains(IntOf(node))),
                    written);
                WriteTable(root, tables, "skinborders.json", assets, null, written);
                break;
            }

            case CatalogSection.Perk:
            {
                var perks = IdsOf(WriteTable(root, tables, "perks.json", assets, null, written));
                WriteTable(root, tables, "perkstyles.json", assets, record => RefersToIds(record, "perks", perks), written);
                break;
            }

            case CatalogSection.Icon:
            {
                var icons = IdsOf(WriteTable(root, tables, "summoner-icons.json", assets, null, written));
                WriteTable(root, tables, "summoner-icon-sets.json", assets, record => RefersToIds(record, "icons", icons), written);
                break;
            }

            case CatalogSection.Ward:
            {
                var wards = IdsOf(WriteTable(root, tables, "ward-skins.json", assets, null, written));
                WriteTable(root, tables, "ward-skin-sets.json", assets, record => RefersToIds(record, "wards", wards), written);
                break;
            }

            default:
                foreach (var table in TablesOf(section)) WriteTable(root, tables, table, assets, null, written);
                break;
        }

        return written;
    }

    /// <summary>Keeps the records whose id list at <paramref name="list"/> touches <paramref name="ids"/>.</summary>
    private static bool RefersToIds(JsonObject record, string list, HashSet<int> ids)
        => record[list] is JsonArray array && array.Any(node => ids.Contains(IntOf(node)));

    /// <summary>
    /// Filters one table and writes it into the root. <paramref name="keep"/> decides per record; without
    /// it a record stays when one of its asset paths points at a file this export wrote.
    /// </summary>
    private static List<JsonObject> WriteTable(string root, CatalogTables tables, string table, ExportPaths assets,
        Func<JsonObject, bool>? keep, List<CatalogJsonFile> written)
    {
        var kept = new List<JsonObject>();
        if (!tables.TryGetTableText(table, out var json)) return kept;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return kept;
        }

        var total = 0;
        if (parsed is JsonObject keyed)
        {
            // skins.json 这类以 id 为键的对象表：键原样保留。
            var copy = new JsonObject();
            foreach (var (key, node) in keyed.ToList())
            {
                total++;
                if (node is not JsonObject record || !Stays(record, keep, assets)) continue;
                if (Finish(record, assets) is not { } cleaned) continue;
                copy[key] = cleaned;
                kept.Add(cleaned);
            }

            Persist(root, table, copy, kept.Count, total, written);
            return kept;
        }

        if (parsed is not JsonArray array) return kept;

        var output = new JsonArray();
        foreach (var node in array)
        {
            total++;
            if (node is not JsonObject record || !Stays(record, keep, assets)) continue;
            if (Finish(record, assets) is not { } cleaned) continue;
            output.Add(cleaned);
            kept.Add(cleaned);
        }

        Persist(root, table, output, kept.Count, total, written);
        return kept;
    }

    private static void Persist(string root, string table, JsonNode node, int records, int total,
        List<CatalogJsonFile> written)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, table);
        MergeFrom(node, path);   // 同一目录多次导出共用这份表，和 info.json 一样
        File.WriteAllText(path, node.ToJsonString(Options), new UTF8Encoding(false));
        written.Add(new CatalogJsonFile(table, path, records, total));
    }

    /// <summary>
    /// Folds the table a previous export left in the same folder into <paramref name="node"/>: what the
    /// same export wrote wins, what an earlier one wrote stays, both back in id order.
    /// </summary>
    private static void MergeFrom(JsonNode node, string path)
    {
        if (!File.Exists(path)) return;

        JsonNode? existing;
        try
        {
            existing = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return;   // 被别的东西改过就当没有
        }

        if (node is JsonObject obj && existing is JsonObject previous)
        {
            foreach (var (key, value) in previous.ToList())
                if (!obj.ContainsKey(key)) obj[key] = value?.DeepClone();
            SortByKey(obj);
        }
        else if (node is JsonArray array && existing is JsonArray old)
        {
            var ids = new HashSet<int>();
            foreach (var item in array)
                if (item is JsonObject record) ids.Add(IdOf(record));
            foreach (var item in old.ToList())
                if (item is JsonObject record && !ids.Contains(IdOf(record))) array.Add(record.DeepClone());
            SortById(array);
        }
    }

    private static void SortByKey(JsonObject obj)
    {
        var pairs = obj.ToList();
        if (pairs.Count < 2 || !pairs.All(pair => int.TryParse(pair.Key, out _))) return;
        var ordered = pairs.OrderBy(pair => int.Parse(pair.Key)).ToList();
        obj.Clear();
        foreach (var (key, value) in ordered) obj[key] = value;
    }

    private static void SortById(JsonArray array)
    {
        if (array.Count < 2 || !array.All(item => item is JsonObject)) return;
        var ordered = array.ToList().OrderBy(item => IdOf(item as JsonObject)).ToList();
        array.Clear();
        foreach (var item in ordered) array.Add(item);
    }

    private static int IdOf(JsonObject? record) => record?["id"] is { } node ? IntOf(node) : 0;

    private static bool Stays(JsonObject record, Func<JsonObject, bool>? keep, ExportPaths assets)
    {
        // id 0 / -1 是客户端的占位记录：universes 与 skinlines 的空行、英雄表里的「无」。
        if (record["id"] is JsonValue value && IntOf(value) <= 0) return false;
        return keep is null ? assets.RefersTo(record) : keep(record);
    }

    /// <summary>
    /// A detached copy of the record with the empty fields dropped and the asset paths rewritten;
    /// null when nothing meaningful is left.
    /// </summary>
    private static JsonObject? Finish(JsonObject record, ExportPaths assets)
    {
        if (JsonNode.Parse(record.ToJsonString()) is not JsonObject copy) return null;
        if (Prune(copy)) return null;
        Rewrite(copy, assets);
        return copy;
    }

    /// <summary>Removes fields with nothing in them in place; true when the node itself is meaningless.</summary>
    private static bool Prune(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return true;
            case JsonValue value:
                return value.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetValue<string>());
            case JsonArray array:
                for (var index = array.Count - 1; index >= 0; index--)
                    if (Prune(array[index])) array.RemoveAt(index);
                return array.Count == 0;
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                    if (Prune(obj[key])) obj.Remove(key);
                return obj.Count == 0;
            default:
                return false;
        }
    }

    /// <summary>Replaces every asset path with the place the exported file has, in place.</summary>
    private static void Rewrite(JsonNode? node, ExportPaths assets)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                    if (!RewriteValue(obj[key], text => obj[key] = text, assets)) Rewrite(obj[key], assets);
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var position = index;
                    if (!RewriteValue(array[position], text => array[position] = text, assets))
                        Rewrite(array[position], assets);
                }
                break;
        }
    }

    private static bool RewriteValue(JsonNode? node, Action<JsonNode?> replace, ExportPaths assets)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String) return false;
        if (!assets.TryRewrite(value.GetValue<string>(), out var rewritten)) return false;
        replace(JsonValue.Create(rewritten));
        return true;
    }

    private static int IntOf(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static HashSet<int> IdsOf(IEnumerable<JsonObject> records)
    {
        var ids = new HashSet<int>();
        foreach (var record in records)
            if (IdOf(record) is var id && id > 0) ids.Add(id);
        return ids;
    }

    /// <summary>ids of the nested objects under <paramref name="list"/>, e.g. a skin's <c>skinLines</c>.</summary>
    private static HashSet<int> IdsIn(IEnumerable<JsonObject> records, string list)
    {
        var ids = new HashSet<int>();
        foreach (var record in records)
        {
            if (record[list] is not JsonArray items) continue;
            foreach (var item in items)
            {
                var id = item is JsonObject nested ? IdOf(nested) : IntOf(item);
                if (id > 0) ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Where every file of one export landed: a lookup from a client asset path to the path the file has
    /// inside the export, matched on the tail of the path so <c>/lol-game-data/assets/ASSETS/…</c> and
    /// <c>plugins/…/global/default/assets/…</c> name the same file.
    /// </summary>
    private sealed class ExportPaths
    {
        private readonly Dictionary<string, string> _byTail = new(StringComparer.OrdinalIgnoreCase);

        public ExportPaths(IReadOnlyList<CatalogEntry> entries, IReadOnlyDictionary<ulong, string> destinations)
        {
            foreach (var entry in entries)
            {
                if (!destinations.TryGetValue(entry.Hash, out var relative)) continue;
                var segments = Segments(entry.Path);
                if (segments.Length == 0) continue;
                relative = relative.Replace('\\', '/');
                for (var count = 2; count <= Math.Min(6, segments.Length); count++)
                    _byTail.TryAdd(Tail(segments, count), relative);
            }
        }

        /// <summary>True when any string of the record is an asset path this export wrote.</summary>
        public bool RefersTo(JsonNode? node)
        {
            switch (node)
            {
                case JsonValue value:
                    return value.GetValueKind() == JsonValueKind.String
                           && IsAssetPath(value.GetValue<string>())
                           && TryResolve(value.GetValue<string>(), out _);
                case JsonArray array:
                    return array.Any(RefersTo);
                case JsonObject obj:
                    return obj.Any(pair => RefersTo(pair.Value));
                default:
                    return false;
            }
        }

        /// <summary>Rewrites an asset path to its place in the export, when the file was exported.</summary>
        public bool TryRewrite(string text, out string rewritten)
        {
            rewritten = text;
            return IsAssetPath(text) && TryResolve(text, out rewritten);
        }

        private bool TryResolve(string path, out string relative)
        {
            var segments = Segments(path);
            for (var count = Math.Min(6, segments.Length); count >= 2; count--)
            {
                if (_byTail.TryGetValue(Tail(segments, count), out relative!)) return true;
            }

            relative = string.Empty;
            return false;
        }

        private static bool IsAssetPath(string text)
            => text.Length > 1 && text[0] == '/' && text.Contains("assets/", StringComparison.OrdinalIgnoreCase);

        private static string[] Segments(string path)
        {
            var lowered = path.Replace('\\', '/').ToLowerInvariant();
            var query = lowered.IndexOf('?');
            if (query >= 0) lowered = lowered[..query];
            const string marker = "lol-game-data/";
            var start = lowered.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0) lowered = lowered[(start + marker.Length)..];
            return lowered.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Tail(string[] segments, int count) => string.Join('/', segments[(segments.Length - count)..]);
    }
}
