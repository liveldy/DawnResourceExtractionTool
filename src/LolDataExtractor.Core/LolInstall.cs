namespace LolDataExtractor.Core;

/// <summary>Finds the wad directory of a League of Legends installation.</summary>
public static class LolInstall
{
    public static IReadOnlyList<string> DefaultCandidates { get; } = new[]
    {
        @"D:\WeGameApps\英雄联盟（含经典模式）\Game\DATA",
        @"D:\WeGameApps\英雄联盟\Game\DATA",
        @"C:\WeGameApps\英雄联盟（含经典模式）\Game\DATA",
        @"C:\Program Files\WeGameApps\英雄联盟\Game\DATA",
        @"C:\Riot Games\League of Legends\Game\DATA",
        @"D:\Riot Games\League of Legends\Game\DATA",
        @"E:\Riot Games\League of Legends\Game\DATA",
        @"D:\Program Files\WeGameApps\英雄联盟\Game\DATA",
    };

    /// <summary>
    /// Normalizes any of these to the DATA root: the DATA folder itself, the Game
    /// folder, the install folder or a FINAL folder.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var path = input.Trim().Trim('"');
        if (!Directory.Exists(path)) return null;

        var full = Path.GetFullPath(path);

        // A FINAL folder belongs to its DATA parent.
        if (string.Equals(Path.GetFileName(full), "FINAL", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(full);
            if (parent is not null && IsWadContainer(parent.FullName)) return parent.FullName;
        }

        if (IsWadContainer(full)) return full;

        foreach (var candidate in new[]
                 {
                     Path.Combine(full, "DATA"),
                     Path.Combine(full, "Game", "DATA"),
                 })
        {
            if (IsWadContainer(candidate)) return candidate;
        }

        var current = Directory.GetParent(full);
        for (var depth = 0; depth < 3 && current is not null; depth++, current = current.Parent)
        {
            if (IsWadContainer(current.FullName)) return current.FullName;
            var gameData = Path.Combine(current.FullName, "Game", "DATA");
            if (IsWadContainer(gameData)) return gameData;
        }

        return null;
    }

    /// <summary>
    /// Resolves the DATA root for <paramref name="hint"/>. When a hint is given it is the
    /// only thing considered: an unusable hint yields null instead of silently jumping to
    /// another installation, which used to reset the folder the user had just typed.
    /// Only without a hint are the well-known install locations probed.
    /// </summary>
    public static string? TryLocate(string? hint = null, IEnumerable<string>? extraCandidates = null)
    {
        if (!string.IsNullOrWhiteSpace(hint)) return Normalize(hint);

        foreach (var candidate in extraCandidates ?? DefaultCandidates)
        {
            if (Normalize(candidate) is { } found) return found;
        }
        return null;
    }

    private static bool IsWadContainer(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        if (Directory.EnumerateFiles(directory, "*.wad.client", SearchOption.TopDirectoryOnly).Any()) return true;
        var final = Path.Combine(directory, "FINAL");
        return Directory.Exists(final) &&
               Directory.EnumerateFiles(final, "*.wad.client", SearchOption.TopDirectoryOnly).Any();
    }

    /// <summary>Every wad below the data root, ordered by relative path.</summary>
    public static IReadOnlyList<string> EnumerateWadFiles(string dataRoot)
    {
        if (!Directory.Exists(dataRoot)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dataRoot, "*.wad.client", SearchOption.AllDirectories)
            .OrderBy(GetRelativeName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetRelativeName(string dataRoot, string wadPath)
        => Path.GetRelativePath(dataRoot, wadPath).Replace('\\', '/');

    public static string GetRelativeName(string wadPath) => Path.GetFileName(wadPath);
}
