using System.Text.RegularExpressions;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// Client json ships once per language folder — <c>global/default/…</c> is the English copy,
/// <c>global/zh_cn/…</c> the Chinese one — so an export has to say which copy it wants. The rule lives
/// here once: the exported files, the transcribed tables and the table reader all ask this class, and
/// the answer follows the language the window is in at the moment of the export.
/// </summary>
public static class CatalogLanguages
{
    /// <summary>Folder the client uses for its English copy.</summary>
    public const string EnglishFolder = "default";

    /// <summary>Folder the client uses for its Chinese copy.</summary>
    public const string ChineseFolder = "zh_cn";

    /// <summary>Client paths are <c>plugins/&lt;plugin&gt;/global/&lt;language&gt;/…</c>.</summary>
    private static readonly Regex ClientLanguageFolder =
        new(@"^plugins/[^/]+/global/([^/]+)/", RegexOptions.IgnoreCase);

    /// <summary>The folder holding the copy for one language.</summary>
    public static string FolderOf(AppLanguage language)
        => language == AppLanguage.English ? EnglishFolder : ChineseFolder;

    /// <summary>The folder the tool is set to right now; read when needed, not at start.</summary>
    public static string CurrentFolder => FolderOf(Strings.Language);

    /// <summary>
    /// Language folder of a client path: <c>…/global/zh_cn/v1/…</c> → <c>zh_cn</c>, <c>…/global/default/…</c>
    /// → <c>default</c>. Empty for every other path (game archives, and client paths without a language).
    /// </summary>
    public static string FolderOfPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var match = ClientLanguageFolder.Match(path);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>The same path without its language folder, i.e. the one file it exists as in every language.</summary>
    public static string LogicalOfPath(string path)
    {
        var match = ClientLanguageFolder.Match(path);
        if (!match.Success) return path;

        var folder = match.Groups[1];
        return path.Remove(folder.Index, folder.Length + 1);   // the folder and the slash after it
    }

    /// <summary>
    /// Keeps one copy per logical file: the one in <paramref name="folder"/> (the window's language),
    /// else the English <c>default</c> copy, else the first that exists. Entries without a language
    /// folder stay untouched, in their original order.
    /// </summary>
    public static IReadOnlyList<CatalogEntry> PickForExport(IEnumerable<CatalogEntry> entries, string? folder = null)
    {
        var preferred = folder ?? CurrentFolder;
        var kept = new List<CatalogEntry>();
        var chosen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var entryFolder = FolderOfPath(entry.Path);
            if (entryFolder.Length == 0)
            {
                kept.Add(entry);
                continue;
            }

            var key = LogicalOfPath(entry.Path);
            if (chosen.TryGetValue(key, out var at))
            {
                kept[at] = Better(kept[at], entry, preferred);
                continue;
            }

            chosen[key] = kept.Count;
            kept.Add(entry);
        }

        return kept;
    }

    /// <summary>True when <paramref name="candidate"/> is the copy worth exporting of the two.</summary>
    private static CatalogEntry Better(CatalogEntry current, CatalogEntry candidate, string preferred)
        => Rank(candidate.Path, preferred) < Rank(current.Path, preferred) ? candidate : current;

    private static int Rank(string path, string preferred)
    {
        var folder = FolderOfPath(path);
        if (string.Equals(folder, preferred, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(folder, EnglishFolder, StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }
}
