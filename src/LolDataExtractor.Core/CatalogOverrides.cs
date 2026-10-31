using System.Text.Json;
using System.Text.Json.Serialization;

namespace LolDataExtractor.Core;

/// <summary>One hand-written rule: everything below a path prefix belongs to this bucket.</summary>
public sealed class CatalogRule
{
    /// <summary>Asset path prefix, e.g. <c>assets/characters/kiwi_</c>; matched case-insensitively.</summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>Category key (<see cref="CatalogCategory"/> name or its Chinese label); empty keeps the automatic one.</summary>
    public string? Category { get; set; }

    /// <summary>Champion alias or a free label shown as the group; empty keeps the automatic one.</summary>
    public string? Champion { get; set; }

    /// <summary>Skin folder or label inside the champion group.</summary>
    public string? Skin { get; set; }

    public bool Hidden { get; set; }

    /// <summary>Why the rule exists; shown in the UI so a later reader does not undo it blindly.</summary>
    public string? Note { get; set; }
}

/// <summary>One hand-written exception for a single entry, addressed by hash.</summary>
public sealed class CatalogEntryRule
{
    public string? Category { get; set; }

    public string? Champion { get; set; }

    public string? Skin { get; set; }

    public bool Hidden { get; set; }
}

/// <summary>
/// The manual safety net of the catalog: what the automatic rules got wrong is written here, and
/// the next build of the index applies it before anything else. Lives next to the other tool data
/// (<c>%LOCALAPPDATA%\LolDataExtractor\catalog-overrides.json</c>) and is saved on every change.
/// </summary>
public sealed class CatalogOverrides
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Keep the Chinese labels readable instead of escaped code points.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();

    public List<CatalogRule> Rules { get; set; } = new();

    /// <summary>Entry exceptions, keyed by the hex path hash.</summary>
    public Dictionary<string, CatalogEntryRule> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string Path { get; private set; } = DefaultPath;

    /// <summary>Where the overrides live; <c>%LOCALAPPDATA%\LolDataExtractor\catalog-overrides.json</c>.</summary>
    public static string DefaultPath => System.IO.Path.Combine(AppSettings.DefaultDataDirectory(), "catalog-overrides.json");

    public static CatalogOverrides Load(string? path = null)
    {
        var target = path ?? DefaultPath;
        try
        {
            if (File.Exists(target))
            {
                var loaded = JsonSerializer.Deserialize<CatalogOverrides>(File.ReadAllText(target), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Rules ??= new List<CatalogRule>();
                    loaded.Entries ??= new Dictionary<string, CatalogEntryRule>(StringComparer.OrdinalIgnoreCase);
                    loaded.Path = target;
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A broken override file must never stop the catalog from opening.
        }

        return new CatalogOverrides { Path = target };
    }

    public void Save()
    {
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
    }

    public bool IsEmpty => Rules.Count == 0 && Entries.Count == 0;

    /// <summary>Rule of one entry, if the user pinned it.</summary>
    public CatalogEntryRule? EntryOf(ulong hash)
    {
        lock (_gate)
        {
            return Entries.TryGetValue(PathHashing.ToText(hash), out var rule) ? rule : null;
        }
    }

    /// <summary>First rule whose prefix matches the path; rules are tried in file order.</summary>
    public CatalogRule? RuleFor(string path)
    {
        lock (_gate)
        {
            foreach (var rule in Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.PathPrefix)) continue;
                if (path.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase)) return rule;
            }
        }

        return null;
    }

    public void AddRule(CatalogRule rule)
    {
        lock (_gate)
        {
            // A new rule for the same prefix replaces the old one: the last decision wins.
            Rules.RemoveAll(existing => string.Equals(existing.PathPrefix, rule.PathPrefix, StringComparison.OrdinalIgnoreCase));
            Rules.Insert(0, rule);
        }

        Save();
    }

    public bool RemoveRule(string pathPrefix)
    {
        bool removed;
        lock (_gate)
        {
            removed = Rules.RemoveAll(rule => string.Equals(rule.PathPrefix, pathPrefix, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed) Save();
        return removed;
    }

    public void SetEntry(ulong hash, CatalogEntryRule? rule)
    {
        var key = PathHashing.ToText(hash);
        lock (_gate)
        {
            if (rule is null) Entries.Remove(key);
            else Entries[key] = rule;
        }

        Save();
    }

    public void Forget(ulong hash)
    {
        bool removed;
        lock (_gate)
        {
            removed = Entries.Remove(PathHashing.ToText(hash));
        }

        if (removed) Save();
    }

    /// <summary>All prefixes, longest first, so the most specific rule is applied first.</summary>
    public IReadOnlyList<CatalogRule> RulesBySpecificity()
    {
        lock (_gate)
        {
            return Rules.OrderByDescending(rule => rule.PathPrefix.Length).ToList();
        }
    }
}
