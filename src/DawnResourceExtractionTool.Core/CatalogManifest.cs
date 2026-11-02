using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DawnResourceExtractionTool.Core;

/// <summary>One file of an export, as the info json lists it.</summary>
public sealed record CatalogInfoEntry(
    string File,
    string Asset,
    string Hash,
    long Size,
    string Wad,
    string Extension);

/// <summary>One catalog entity of an export: the node it belongs to, its folder and its files.</summary>
public sealed record CatalogInfoEntity(
    string Section,
    string Group,
    string Entity,
    string Anchor,
    string Folder,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<CatalogInfoEntry> Files);

/// <summary>
/// The single <c>info.json</c> written at the root of an export: every entity the export wrote, the
/// node each one belongs to, and the origin, hash and size of every file. An exported tree then
/// explains itself after it has been moved away from the tool, without one json per folder.
/// </summary>
public sealed record CatalogInfoDocument(
    string GeneratedAt,
    string Tool,
    int EntityCount,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<CatalogInfoEntity> Entities);

/// <summary>Serialization of the catalog export info file, written once per export root.</summary>
public static class CatalogInfo
{
    /// <summary>File written at the root of an export.</summary>
    public const string FileName = "info.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes <paramref name="entities"/> into <c>root\info.json</c>. Entities already listed there by
    /// an earlier export stay, so several exports into one output folder share a single summary; a
    /// re-exported entity replaces its own entry.
    /// </summary>
    public static CatalogInfoDocument Write(string root, string tool, IEnumerable<CatalogInfoEntity> entities)
    {
        var path = Path.Combine(root, FileName);

        var merged = new Dictionary<string, CatalogInfoEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in Read(path)?.Entities ?? Array.Empty<CatalogInfoEntity>())
            merged[entity.Folder] = entity;
        foreach (var entity in entities)
            merged[entity.Folder] = entity;

        var ordered = merged.Values.OrderBy(entity => entity.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        var document = new CatalogInfoDocument(
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            tool,
            ordered.Count,
            ordered.Sum(entity => entity.FileCount),
            ordered.Sum(entity => entity.TotalBytes),
            ordered);

        Directory.CreateDirectory(root);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
        return document;
    }

    /// <summary>Reads a written document back; used by the tests and by anyone re-reading an export.</summary>
    public static CatalogInfoDocument? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CatalogInfoDocument>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;   // someone replaced it with something else: start the summary over
        }
    }
}
