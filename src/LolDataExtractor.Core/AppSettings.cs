using System.Text.Json;
using System.Text.Json.Serialization;

namespace LolDataExtractor.Core;

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

    public static string DefaultDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LolDataExtractor");

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

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), SerializerOptions);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.DataDirectory)) loaded.DataDirectory = DefaultDataDirectory();
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
