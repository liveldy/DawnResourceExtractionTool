using System.Text.Json;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// Editable text notes attached to audio streams, keyed by wad + entry hash + stream index so
/// they survive a patch that renames the asset folder. Stored as one small json file next to the
/// other tool data and written whenever a note changes.
/// </summary>
public sealed class AudioNoteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Keep the Chinese readable in the file instead of escaped code points.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private AudioNoteStore(string path)
    {
        Path = path;
    }

    /// <summary>Where the notes live; <c>%LOCALAPPDATA%\DawnResourceExtractionTool\audio-notes.json</c>.</summary>
    public static string DefaultPath => System.IO.Path.Combine(AppSettings.DefaultDataDirectory(), "audio-notes.json");

    public string Path { get; }

    public int Count
    {
        get
        {
            lock (_gate) return _notes.Count;
        }
    }

    /// <summary>Stable key of one stream: wad file name, entry hash and 1-based stream number.</summary>
    public static string KeyOf(string wadPath, ulong entryHash, int streamIndex) =>
        $"{System.IO.Path.GetFileName(wadPath)}|{PathHashing.ToText(entryHash)}|{streamIndex}";

    public static AudioNoteStore Load(string? path = null)
    {
        var store = new AudioNoteStore(path ?? DefaultPath);
        try
        {
            if (File.Exists(store.Path))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(store.Path));
                if (saved is not null)
                {
                    foreach (var (key, value) in saved)
                    {
                        if (!string.IsNullOrWhiteSpace(value)) store._notes[key] = value;
                    }
                }
            }
        }
        catch (Exception)
        {
            // A corrupt note file only costs the notes; editing starts from an empty set.
        }
        return store;
    }

    public string? Get(string key)
    {
        lock (_gate) return _notes.TryGetValue(key, out var note) ? note : null;
    }

    public void Set(string key, string? note)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                if (!_notes.Remove(key)) return;
            }
            else
            {
                if (_notes.TryGetValue(key, out var current) && current == note) return;
                _notes[key] = note.Trim();
            }
        }
        Save();
    }

    /// <summary>Writes the whole note set; a failed write keeps the in-memory notes intact.</summary>
    public void Save()
    {
        Dictionary<string, string> snapshot;
        lock (_gate) snapshot = new Dictionary<string, string>(_notes, StringComparer.Ordinal);

        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions), new System.Text.UTF8Encoding(false));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception)
        {
            // Notes stay in memory for the rest of the session.
        }
    }
}
