namespace DawnResourceExtractionTool.Core;

/// <summary>
/// The client side of an installation: <c>LeagueClient\plugins\&lt;plugin&gt;\*.wad</c>. These are the
/// same wad v3 containers as the game archives, but a separate name space — entries are hashed
/// paths below <c>plugins/&lt;plugin&gt;/global/&lt;locale&gt;/…</c>, resolved with CommunityDragon's
/// <c>hashes.lcu.txt</c> instead of the game hash table. That is where the client keeps the champion
/// and item text, the icon paths, the portraits and the skin splash art.
/// </summary>
public static class LolClient
{
    /// <summary>Prefix of client wads in the wad list, so they group apart from the DATA tree.</summary>
    public const string RelativePrefix = "LeagueClient";

    public const string ClientFolder = "LeagueClient";

    public const string PluginsFolder = "plugins";

    /// <summary>
    /// Resolves the <c>…\LeagueClient\plugins</c> folder from the folder the user picked. Only what
    /// that folder itself encloses counts: the folder when it is a plugins folder, its
    /// <c>plugins</c> child, or its <c>LeagueClient\plugins</c> child. Nothing above it is probed, so
    /// picking the install folder scans both sides while picking <c>Game\DATA</c> scans the game
    /// archives alone. Returns null when no client wad lives there.
    /// </summary>
    public static string? TryLocatePlugins(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;

        var raw = hint.Trim().Trim('"');
        var start = File.Exists(raw) ? Path.GetDirectoryName(Path.GetFullPath(raw)) : raw;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start)) return null;

        var folder = Path.GetFullPath(start);
        foreach (var candidate in new[]
                 {
                     folder,
                     Path.Combine(folder, PluginsFolder),
                     Path.Combine(folder, ClientFolder, PluginsFolder),
                 })
        {
            if (HasWads(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsPluginsFolder(string path) =>
        string.Equals(Path.GetFileName(path.TrimEnd('\\', '/')), PluginsFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the folder itself (or one level below) holds a client wad.</summary>
    private static bool HasWads(string directory)
    {
        if (!Directory.Exists(directory) || !IsPluginsFolder(directory)) return false;
        try
        {
            return Directory.EnumerateFiles(directory, "*.wad", SearchOption.AllDirectories).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Every <c>*.wad</c> below the plugins folder, ordered by path.</summary>
    public static IReadOnlyList<string> EnumerateWadFiles(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(pluginsRoot, "*.wad", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Where a client wad sits in the wad list: <c>LeagueClient\&lt;plugin&gt;\&lt;file&gt;.wad</c>.</summary>
    public static string RelativePath(string pluginsRoot, string wadPath)
    {
        var relative = Path.GetRelativePath(pluginsRoot, wadPath).Replace('\\', '/');
        return $"{RelativePrefix}/{relative}";
    }
}
