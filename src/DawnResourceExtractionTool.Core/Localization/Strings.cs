using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DawnResourceExtractionTool.Core.Localization;

/// <summary>
/// Everything the user reads comes from here: one key, one line per language, taken from the
/// <c>Localization/strings.*.json</c> files embedded in this assembly. Code calls
/// <see cref="T(string)"/>, XAML uses the <c>{loc:Loc Key}</c> markup extension, and both follow
/// <see cref="Language"/>; switching it raises <see cref="Changed"/> so the window redraws in place.
/// </summary>
public static class Strings
{
    private const string ResourceMarker = ".Localization.strings.";

    private static readonly Dictionary<string, Entry> Entries = Load();
    private static readonly List<string> Missing = new();
    private static AppLanguage _language = AppLanguage.Chinese;

    /// <summary>Raised after the language changed; views refresh their texts from here.</summary>
    public static event Action? Changed;

    /// <summary>Language the tool speaks. Setting it to the current value does nothing.</summary>
    public static AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            CultureInfo.CurrentUICulture = value == AppLanguage.English
                ? new CultureInfo("en-US")
                : new CultureInfo("zh-CN");
            Changed?.Invoke();
        }
    }

    /// <summary>Keys unknown to the json files, collected while the tool runs (the tests check this).</summary>
    public static IReadOnlyList<string> MissingKeys => Missing;

    /// <summary>All keys of every embedded json file, with both languages.</summary>
    public static IReadOnlyDictionary<string, (string Chinese, string English)> All
        => Entries.ToDictionary(pair => pair.Key, pair => (pair.Value.Chinese, pair.Value.English));

    /// <summary>The text for a key in the current language; missing keys come back as the key itself.</summary>
    public static string T(string key)
    {
        if (Entries.TryGetValue(key, out var entry))
        {
            var text = _language == AppLanguage.English ? entry.English : entry.Chinese;
            if (!string.IsNullOrEmpty(text)) return text;
            return string.IsNullOrEmpty(entry.Chinese) ? key : entry.Chinese;
        }

        lock (Missing)
        {
            if (!Missing.Contains(key)) Missing.Add(key);
        }

        return key;
    }

    /// <summary>The text for a key with <see cref="string.Format(string, object?[])"/> placeholders.</summary>
    public static string T(string key, params object?[] args)
        => args.Length == 0 ? T(key) : string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>True when the key exists (used by the tests and the self test).</summary>
    public static bool Has(string key) => Entries.ContainsKey(key);

    private static Dictionary<string, Entry> Load()
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var assembly = typeof(Strings).Assembly;

        foreach (var name in assembly.GetManifestResourceNames().OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!name.Contains(ResourceMarker, StringComparison.Ordinal)) continue;

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            try
            {
                Read(reader.ReadToEnd(), entries);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"本地化文件损坏：{name}", ex);
            }
        }

        return entries;
    }

    private static void Read(string json, Dictionary<string, Entry> entries)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var chinese = string.Empty;
            var english = string.Empty;

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (property.Value.TryGetProperty("zh", out var zh) && zh.ValueKind == JsonValueKind.String)
                    chinese = zh.GetString() ?? string.Empty;
                if (property.Value.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String)
                    english = en.GetString() ?? string.Empty;
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                // A plain string is the same text in every language (numbers, file names, symbols).
                chinese = english = property.Value.GetString() ?? string.Empty;
            }

            entries[property.Name] = new Entry(chinese, english);
        }
    }

    private readonly record struct Entry(string Chinese, string English);
}
