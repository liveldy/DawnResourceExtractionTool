using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>Asset candidates that belong to one model, best guess first.</summary>
public sealed record LinkedAssets(
    WadEntryInfo Model,
    IReadOnlyList<WadEntryInfo> Skeletons,
    IReadOnlyList<WadEntryInfo> Animations,
    IReadOnlyList<WadEntryInfo> Textures)
{
    public WadEntryInfo? DefaultSkeleton => Skeletons.FirstOrDefault();

    /// <summary>Idle reads best as a default, otherwise the first clip of the folder.</summary>
    public WadEntryInfo? DefaultAnimation =>
        Animations.FirstOrDefault(entry => BaseNameOf(entry).Equals("idle", StringComparison.OrdinalIgnoreCase))
        ?? Animations.FirstOrDefault(entry => BaseNameOf(entry).StartsWith("idle", StringComparison.OrdinalIgnoreCase))
        ?? Animations.FirstOrDefault();

    public WadEntryInfo? DefaultTexture => Textures.FirstOrDefault();

    public string Describe =>
        Strings.T("Core.Linker.Describe", Skeletons.Count, Animations.Count, Textures.Count);

    private static string BaseNameOf(WadEntryInfo entry)
    {
        var name = entry.FileName;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}

/// <summary>
/// Guesses which skeleton, animations and texture belong to a model by looking at the
/// shipped folder layout: a skin keeps its <c>.skn</c>/<c>.skl</c>/<c>_tx_cm.tex</c> in one
/// folder and its clips in an <c>animations</c> subfolder next to it.
/// </summary>
public static class AssetLinker
{
    private const int MaxAnimations = 400;

    public static LinkedAssets Find(WadEntryInfo model, IEnumerable<WadEntryInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(entries);

        var path = NormalizePath(model.Path);
        var directory = DirectoryOf(path);
        var baseName = BaseNameOf(path);
        var championRoot = ChampionRootOf(path);
        var animationFolder = directory + "/animations";
        var baseAnimationFolder = BaseSkinFolder(championRoot) + "/animations";
        var inParticles = path.Contains("/particles/", StringComparison.Ordinal);

        var skeletons = new List<(int Score, string Path, WadEntryInfo Entry)>();
        var animations = new List<(int Score, string Path, WadEntryInfo Entry)>();
        var textures = new List<(int Score, string Path, WadEntryInfo Entry)>();

        foreach (var entry in entries)
        {
            var candidate = NormalizePath(entry.Path);
            if (candidate.Length == 0) continue;

            var candidateDirectory = DirectoryOf(candidate);
            var candidateBase = BaseNameOf(candidate);
            var sameDirectory = candidateDirectory.Equals(directory, StringComparison.OrdinalIgnoreCase);

            switch (entry.Extension)
            {
                case "skl":
                {
                    var score = sameDirectory && candidateBase.Equals(baseName, StringComparison.OrdinalIgnoreCase) ? 0
                        : sameDirectory ? 1
                        : candidateBase.Equals(baseName, StringComparison.OrdinalIgnoreCase) && candidate.StartsWith(championRoot, StringComparison.OrdinalIgnoreCase) ? 3
                        : candidate.StartsWith(championRoot, StringComparison.OrdinalIgnoreCase) && !candidate.Contains("/particles/", StringComparison.Ordinal) ? 5
                        : -1;
                    if (score >= 0) skeletons.Add((score, candidate, entry));
                    break;
                }

                case "anm":
                {
                    if (!candidate.StartsWith(championRoot, StringComparison.OrdinalIgnoreCase)) break;

                    var score =
                        candidateDirectory.Equals(animationFolder, StringComparison.OrdinalIgnoreCase) ? 0
                        : sameDirectory ? 1
                        : candidate.StartsWith(baseAnimationFolder + "/", StringComparison.OrdinalIgnoreCase) ? 4
                        : -1;
                    if (score >= 0) animations.Add((score, candidate, entry));
                    break;
                }

                default:
                {
                    if (entry.Extension is not ("tex" or "dds" or "png" or "tga")) break;
                    if (!sameDirectory) break;
                    if (!inParticles && candidate.Contains("/particles/", StringComparison.Ordinal)) break;

                    var score = candidateBase.Equals(baseName + "_tx_cm", StringComparison.OrdinalIgnoreCase) ? 0
                        : candidateBase.StartsWith(baseName + "_tx_", StringComparison.OrdinalIgnoreCase) ? 1
                        : candidateBase.Equals(baseName, StringComparison.OrdinalIgnoreCase) ? 1
                        : 3;
                    textures.Add((score, candidate, entry));
                    break;
                }
            }
        }

        // A skin with its own animation folder never falls back to the base skin's clips.
        var ownAnimations = animations.Where(candidate => candidate.Score <= 1).ToList();
        var chosenAnimations = ownAnimations.Count > 0 ? ownAnimations : animations;

        return new LinkedAssets(
            model,
            Pick(skeletons).ToList(),
            Pick(chosenAnimations).Take(MaxAnimations).ToList(),
            Pick(textures).ToList());
    }

    /// <summary>File name of a model without its folder and extension.</summary>
    private static IEnumerable<WadEntryInfo> Pick(List<(int Score, string Path, WadEntryInfo Entry)> candidates)
        => candidates
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Entry);

    private static string NormalizePath(string? path)
        => string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').ToLowerInvariant();

    private static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string BaseNameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        var name = slash < 0 ? path : path[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>"assets/characters/ahri/skins/base/x.skn" → "assets/characters/ahri".</summary>
    private static string ChampionRootOf(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3 && segments[0] == "assets" && segments[1] == "characters")
            return string.Join('/', segments.Take(3));
        return DirectoryOf(path);
    }

    private static string BaseSkinFolder(string championRoot) => championRoot + "/skins/base";
}
