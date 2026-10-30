namespace LolDataExtractor.Core;

/// <summary>
/// Picks a texture for every submesh of a model. A skin ships its parts as separate files
/// (<c>ahri_base_tx_cm</c>, <c>ahri_base_tails_tx_cm</c>) while the mesh only names its
/// submeshes (<c>Body</c>, <c>Tails</c>), so a texture is bound to the submesh whose name
/// explains the words the texture adds beyond the model name.
/// <para>
/// The rules come from the naming convention of the game's art pipeline, the only signal the
/// shipped files carry:
/// </para>
/// <list type="bullet">
/// <item>a texture name ends with its map kind — <c>_tx_cm</c> / <c>_cm</c> / <c>_tx</c> is the
/// colour map, while <c>mask</c>, <c>rmsa</c>, <c>ramp</c>, <c>loadscreen</c> and friends are data;</item>
/// <item>a texture that adds no word at all is the model's own colour map;</item>
/// <item>part names are words glued together (<c>Ultcloth01</c>, <c>Tails_MAT</c>, <c>Tailset1</c>),
/// so names are compared word by word with camel-case, digit and plural folding;</item>
/// <item><c>body</c> / <c>main</c> / <c>default</c> describe the model itself, not a part, so they
/// never stop a texture from being bound (<c>gwen_base_main_doll_tx_cm</c> is the doll);</item>
/// <item>a short word only matches as a prefix: <c>e</c> is <c>Eyes</c>, while the ability map
/// <c>..._r_tx</c> does not become the <c>Hair</c> texture just because "hair" ends in r.</item>
/// </list>
/// A data map is never drawn on a mesh, and a folder that holds nothing but data maps leaves the
/// model untextured instead of covering it with a mask or a loading screen.
/// </summary>
public static class TextureBinder
{
    /// <summary>Returned for a part that has no usable texture at all.</summary>
    public const int NoTexture = -1;

    /// <summary>Endings that mark a texture as a colour map.</summary>
    private static readonly HashSet<string> ColourEndings = new(StringComparer.Ordinal)
    {
        "cm", "cm2", "cm3", "cms", "tx", "dds", "tex", "diffuse", "basecolor", "albedo",
    };

    /// <summary>Words that may follow a colour ending (<c>_tx_cm_alpha</c>, <c>_tx_cm_classic</c>).</summary>
    private static readonly HashSet<string> EndingModifiers = new(StringComparer.Ordinal)
    {
        "alpha", "classic", "premul", "blank",
    };

    /// <summary>Fragments that only ever appear in the name of a data map.</summary>
    private static readonly string[] DataFragments =
    {
        "mask", "deform", "rmsa", "osam", "ramp", "scroll", "loadscreen", "glow", "matcap", "gradient",
        "noise", "dissolve", "erosion", "emissive", "specular", "chromapreview", "color-hold", "colorhold",
    };

    /// <summary>Short data map words that are matched as whole words.</summary>
    private static readonly HashSet<string> DataWords = new(StringComparer.Ordinal)
    {
        "rma", "em", "gm", "le", "nrm", "mult", "ao",
    };

    /// <summary>Words the main body texture carries; they make a texture the default one.</summary>
    private static readonly HashSet<string> BodyWords = new(StringComparer.Ordinal)
    {
        "body", "main", "default", "base",
    };

    private enum Role
    {
        /// <summary>The naming convention says this is a colour map.</summary>
        Colour,

        /// <summary>No suffix and no data word: usable, but never the model's main texture.</summary>
        Unknown,

        /// <summary>A mask, roughness map, ramp or promo image — never drawn on a mesh.</summary>
        Data,
    }

    private readonly record struct Candidate(string Stem, string[] Extra, Role Role);

    /// <summary>
    /// Returns, for each part, the index of the texture to draw it with, or
    /// <see cref="NoTexture"/> when the model has no colour map to bind.
    /// </summary>
    /// <param name="partNames">Submesh names, e.g. <c>Body</c>, <c>Tails</c>.</param>
    /// <param name="textureStems">Texture file names without their extension.</param>
    /// <param name="modelStem">Model file name without its extension, e.g. <c>ahri_base</c>.</param>
    public static int[] Assign(IReadOnlyList<string> partNames, IReadOnlyList<string> textureStems, string modelStem)
    {
        ArgumentNullException.ThrowIfNull(partNames);
        ArgumentNullException.ThrowIfNull(textureStems);

        var result = new int[partNames.Count];
        if (textureStems.Count == 0)
        {
            Array.Fill(result, NoTexture);
            return result;
        }

        var modelTokens = Tokens(modelStem).Select(TokenStem).ToHashSet(StringComparer.Ordinal);
        var candidates = textureStems.Select(stem => Analyze(stem, modelTokens)).ToArray();

        // Data maps take no part in the binding: a folder that only holds them leaves the model
        // untextured rather than painting a mask or a loading screen over it.
        var usable = Enumerable.Range(0, candidates.Length)
            .Where(index => candidates[index].Role != Role.Data)
            .ToArray();
        if (usable.Length == 0)
        {
            Array.Fill(result, NoTexture);
            return result;
        }

        var fallback = usable[0];
        foreach (var index in usable.Skip(1))
        {
            var candidate = candidates[index];
            var current = candidates[fallback];
            var better = candidate.Role < current.Role
                || (candidate.Role == current.Role
                    && (DefaultRank(candidate) < DefaultRank(current)
                        || (DefaultRank(candidate) == DefaultRank(current)
                            && candidate.Extra.Length < current.Extra.Length)));
            if (better) fallback = index;
        }

        for (var part = 0; part < partNames.Count; part++)
        {
            var partTokens = Tokens(partNames[part])
                .Select(TokenStem)
                .Where(token => !modelTokens.Contains(token))
                .ToArray();

            var best = fallback;
            var bestScore = 0;
            var bestRole = Role.Data;
            var bestExtra = int.MaxValue;

            foreach (var index in usable)
            {
                var candidate = candidates[index];
                var score = Score(candidate.Extra, partTokens);
                if (score == 0) continue;

                // On a tie the texture that adds fewer words wins: it is the plainer, more
                // general map, and the words it does add are the ones that matched.
                var better = score > bestScore
                    || (score == bestScore && (candidate.Role < bestRole
                        || (candidate.Role == bestRole && candidate.Extra.Length < bestExtra)));
                if (!better) continue;

                best = index;
                bestScore = score;
                bestRole = candidate.Role;
                bestExtra = candidate.Extra.Length;
            }

            result[part] = best;
        }

        return result;
    }

    /// <summary>
    /// Splits a texture name into the words it adds beyond the model name and classifies the map
    /// it carries.
    /// </summary>
    private static Candidate Analyze(string stem, HashSet<string> modelTokens)
    {
        var tokens = Tokens(stem);

        var end = tokens.Length - 1;

        // A trailing modifier belongs to the map kind only when a colour ending precedes it
        // ("..._tx_cm_alpha" is a colour map, "..._horse_alpha" is an alpha mask).
        while (end > 0 && EndingModifiers.Contains(tokens[end]) && ColourEndings.Contains(tokens[end - 1])) end--;

        var colourEnding = false;
        while (end >= 0 && ColourEndings.Contains(tokens[end]))
        {
            colourEnding = true;
            end--;
        }

        var words = tokens[..(end + 1)];
        var extra = words.Select(TokenStem).Where(token => !modelTokens.Contains(token)).ToArray();

        var joined = string.Join('_', words);
        var data = DataFragments.Any(fragment => joined.Contains(fragment, StringComparison.Ordinal))
            || words.Any(DataWords.Contains)
            || (words.Length > 0 && words[^1] == "alpha");

        // A texture with no extra word is the model's own colour map, whatever its name happens
        // to contain ("brand_gasmask_tx_cm" is a gas mask, not a mask map).
        var role = extra.Length == 0 ? Role.Colour
            : data ? Role.Data
            : colourEnding ? Role.Colour
            : Role.Unknown;

        return new Candidate(stem, extra, role);
    }

    /// <summary>Lower ranks make better default textures (own colour map, then body, then the rest).</summary>
    private static int DefaultRank(Candidate candidate)
        => candidate.Extra.Length == 0 ? 0
            : candidate.Extra.Any(BodyWords.Contains) ? 1
            : 2;

    /// <summary>
    /// Scores a texture against a part name: every word the texture adds has to be explained by
    /// the part, and a name that matches token for token scores highest.
    /// </summary>
    private static int Score(string[] extra, string[] part)
    {
        if (part.Length == 0) return 0;

        var words = extra.Where(token => !IsNumber(token)).ToArray();
        if (words.Length == 0) return 0;

        var total = 0;
        var matched = 0;
        foreach (var word in words)
        {
            if (part.Contains(word, StringComparer.Ordinal))
            {
                total += 2;
                matched++;
                continue;
            }

            // "body", "main" and "default" say "this is the model's own part", not which part,
            // so they never block a match: "gwen_base_main_doll_tx_cm" belongs to the doll.
            if (BodyWords.Contains(word)) continue;

            if (!MatchesWord(word, part)) return 0;

            total += 1;
            matched++;
        }

        // Only generic words were explained: that is the model's default map, chosen as fallback.
        if (matched == 0) return 0;

        // "Ezreal_Lv3_Mat" must not take the Lv4 texture: the numbers have to agree too.
        var extraNumbers = Numbers(extra);
        var partNumbers = Numbers(part);
        if (extraNumbers.Length > 0 && partNumbers.Length > 0 && extraNumbers != partNumbers) return 0;

        var partWords = part.Where(token => !IsNumber(token)).ToArray();
        var exact = words.Length == partWords.Length && words.All(word => partWords.Contains(word, StringComparer.Ordinal));
        return (exact ? 100 : 60) + total;
    }

    /// <summary>
    /// A word is explained by a part token that starts with it (<c>e</c> → <c>Eyes</c>,
    /// <c>tail</c> → <c>Tails</c>) or, for words of three letters and up, one that ends with it
    /// (<c>wing</c> → <c>CrystalWings</c>). Short words only match as a prefix, so the ability
    /// map <c>..._r_tx</c> does not land on a part named <c>Hair</c>.
    /// </summary>
    private static bool MatchesWord(string word, string[] part)
        => part.Any(token => token.StartsWith(word, StringComparison.Ordinal)
            || (word.Length >= 3 && token.Length >= 3 && token.EndsWith(word, StringComparison.Ordinal)));

    private static string Numbers(IEnumerable<string> tokens)
        => string.Join(',', tokens.Where(IsNumber).OrderBy(token => token, StringComparer.Ordinal));

    private static bool IsNumber(string token) => token.Length > 0 && token.All(char.IsAsciiDigit);

    /// <summary>
    /// Splits a name into lower-case words: camel case and letter/digit boundaries start a new
    /// word, and so does every separator (<c>_</c>, <c>-</c>, <c>.</c>, <c>:</c>, whitespace).
    /// </summary>
    private static string[] Tokens(string name)
    {
        var words = new List<string>();
        var start = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (!char.IsAsciiLetterOrDigit(current))
            {
                Flush(words, name, start, i);
                start = i + 1;
                continue;
            }

            if (i == start) continue;

            var previous = name[i - 1];
            var boundary = (char.IsAsciiLetterLower(previous) && char.IsAsciiLetterUpper(current))
                || (char.IsAsciiLetter(previous) && char.IsAsciiDigit(current));
            if (!boundary) continue;

            Flush(words, name, start, i);
            start = i;
        }

        Flush(words, name, start, name.Length);
        return words.ToArray();

        static void Flush(List<string> words, string name, int start, int end)
        {
            if (end > start) words.Add(name[start..end].ToLowerInvariant());
        }
    }

    /// <summary>Folds a plural so "tails" still matches "tail".</summary>
    private static string TokenStem(string token)
        => token.Length > 3 && token.EndsWith('s') ? token[..^1] : token;
}
