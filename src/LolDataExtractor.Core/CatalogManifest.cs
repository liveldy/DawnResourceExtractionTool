using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LolDataExtractor.Core;

/// <summary>One file of an export, as the info json lists it.</summary>
public sealed record CatalogInfoEntry(
    string File,
    string Asset,
    string Hash,
    long Size,
    string Wad,
    string Extension);

/// <summary>
/// The <c>info.json</c> written beside the files of one catalog entity: which node they belong to,
/// where they came from and how big they are. An exported folder then explains itself even after it
/// has been moved away from the tool.
/// </summary>
public sealed record CatalogInfoDocument(
    string GeneratedAt,
    string Tool,
    string Section,
    string Group,
    string Entity,
    string Anchor,
    string Folder,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<CatalogInfoEntry> Files);

/// <summary>Name, name clash handling and serialization of the catalog export info file.</summary>
public static class CatalogInfo
{
    /// <summary>File written into every entity folder of an export.</summary>
    public const string FileName = "info.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Name to use in a folder that holds <paramref name="relativePaths"/>: <c>info.json</c>, or
    /// <c>_info.json</c> (and so on) when an exported asset of the same folder is called the same.
    /// </summary>
    public static string NameFor(IEnumerable<string> relativePaths)
    {
        var taken = new HashSet<string>(relativePaths, StringComparer.OrdinalIgnoreCase);
        var candidate = FileName;
        while (taken.Contains(candidate)) candidate = "_" + candidate;
        return candidate;
    }

    /// <summary>Writes the document into <paramref name="folder"/>; returns the path written.</summary>
    public static string Write(string folder, CatalogInfoDocument document, string? fileName = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName ?? FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
        return path;
    }

    /// <summary>Reads a written document back; used by the tests and by anyone re-reading an export.</summary>
    public static CatalogInfoDocument? Read(string path)
        => File.Exists(path) ? JsonSerializer.Deserialize<CatalogInfoDocument>(File.ReadAllText(path), Options) : null;
}
