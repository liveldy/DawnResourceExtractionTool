using System.Text.Json;
using System.Text.Json.Serialization;

namespace DawnResourceExtractionTool.Core;

/// <summary>Persisted user preferences (game folder, output folder, hash table folder).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? DataRoot { get; set; }

    public string? OutputRoot { get; set; }

    /// <summary>Folder holding the downloaded hash tables and the generated name index.</summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    /// <summary>UI colours: <c>Dark</c> (the default), <c>Light</c> or <c>System</c>.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>UI language: <c>zh</c> or <c>en</c>; null follows the system language.</summary>
    public string? Language { get; set; }

    public static string DefaultDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DawnResourceExtractionTool");

    /// <summary>
    /// The folder this program used before it was renamed. Its hash tables and name index are hundreds
    /// of megabytes, so the first start of the renamed build moves them over instead of downloading again.
    /// </summary>
    private static string LegacyDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LolDataExtractor");

    /// <summary>Moves the data folder of the previous name into place; safe to call on every start.</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var legacy = LegacyDataDirectory();
            var current = DefaultDataDirectory();
            if (!Directory.Exists(legacy) || Directory.Exists(current)) return;

            var parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            Directory.Move(legacy, current);
        }
        catch (Exception)
        {
            // A failed move is only an extra download, never a reason to refuse starting.
        }
    }

    /// <summary>Extracted assets land on the desktop, next to where people keep their own files.</summary>
    public static string DefaultOutputDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(desktop, "LoLAssets");
    }

    public static string SettingsPath
        => Path.Combine(DefaultDataDirectory(), "settings.json");

    public string HashTableDirectory => Path.Combine(DataDirectory, "hashtable");

    public string NamesIndexPath => Path.Combine(HashTableDirectory, "names.idx");

    /// <summary>Folder of the client name table, kept apart so the game table build never picks it up.</summary>
    [JsonIgnore]
    public string ClientTableDirectory => Path.Combine(HashTableDirectory, "client");

    [JsonIgnore]
    public string ClientNamesPath => Path.Combine(ClientTableDirectory, "client-names.idx");

    public static AppSettings Load()
    {
        MigrateLegacyData();

        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), SerializerOptions);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.DataDirectory)) loaded.DataDirectory = DefaultDataDirectory();

                    // Settings written before the rename carry the old absolute folder; point them at the new one.
                    var legacy = LegacyDataDirectory();
                    if (loaded.DataDirectory.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
                        loaded.DataDirectory = DefaultDataDirectory() + loaded.DataDirectory[legacy.Length..];

                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A broken settings file must never block startup.
        }

        var settings = new AppSettings();
        settings.DataRoot = LolInstall.TryLocate();
        settings.OutputRoot = DefaultOutputDirectory();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}
