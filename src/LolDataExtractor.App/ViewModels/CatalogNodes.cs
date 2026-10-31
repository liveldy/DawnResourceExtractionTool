using System.IO;
using System.Collections.ObjectModel;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.ViewModels;

/// <summary>
/// One row of the classified tree: a category, a hero, a skin, a folder or a file. Children are
/// built on first expansion — the catalog holds ~900k files and must never build them all.
/// </summary>
public sealed class CatalogNodeVm : ObservableObject
{
    private readonly Func<CatalogNodeVm, Task>? _expand;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _loaded;
    private bool _isLoading;

    private CatalogNodeVm(
        string name,
        string countText,
        bool isFolder,
        CatalogEntry? entry,
        CatalogCategory? category,
        int hero,
        int skin,
        Func<CatalogNodeVm, Task>? expand,
        string? hint = null,
        string? toolTip = null)
    {
        Name = name;
        CountText = countText;
        IsFolder = isFolder;
        Entry = entry;
        Category = category;
        Hero = hero;
        Skin = skin;
        _expand = expand;
        Hint = hint;
        ToolTip = toolTip ?? name;
    }

    public string Name { get; }

    /// <summary>Trailing text: file count for a folder, size for a file.</summary>
    public string CountText { get; }

    /// <summary>Small warning next to the name, e.g. "推测" or "目录未识别".</summary>
    public string? Hint { get; }

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public string ToolTip { get; }

    public bool IsFolder { get; }

    /// <summary>Set on file rows only.</summary>
    public CatalogEntry? Entry { get; }

    /// <summary>Bucket this node belongs to; <c>null</c> on the "search results" root.</summary>
    public CatalogCategory? Category { get; }

    public int Hero { get; }

    public int Skin { get; }

    public ObservableCollection<CatalogNodeVm> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            if (value) _ = EnsureLoadedAsync();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>True while children are being built, so the UI can say so.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded || _expand is null) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            await _expand(this);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public static CatalogNodeVm ForCategory(CatalogCategory category, int files, int heroes,
        Func<CatalogNodeVm, Task> expand, string description) =>
        new(CatalogCategoryInfo.Of(category).Display, $"{files:N0} 个文件 · {heroes:N0} 个分组", true, null, category, -1, -1, expand,
            toolTip: description);

    public static CatalogNodeVm ForHero(string name, string countText, CatalogCategory category, int hero, bool guessed, bool folder,
        Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, category, hero, -1, expand,
            hint: guessed ? "推测" : folder ? "目录未识别" : null);

    public static CatalogNodeVm ForSkin(string name, string countText, CatalogCategory category, int hero, int skin,
        Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, category, hero, skin, expand);

    public static CatalogNodeVm ForFolder(string name, string countText, Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, null, -1, -1, expand);

    public static CatalogNodeVm ForFile(CatalogEntry entry) =>
        new(entry.FileName, ExtractionSummary.Format(entry.Size), false, entry, null, -1, -1, null,
            toolTip: entry.Path + (entry.WadCount > 1 ? $"\n（{entry.WadCount} 个 wad 含此条目，打开的是 {Path.GetFileName(entry.WadPath)}）" : string.Empty));

    /// <summary>A plain message row (nothing to show, a truncated list, a search hint).</summary>
    public static CatalogNodeVm Message(string text) =>
        new(text, string.Empty, false, null, null, -1, -1, null);

    public static CatalogNodeVm SearchRoot(string query, int total, Func<CatalogNodeVm, Task> expand) =>
        new($"搜索结果：{query}", $"{total:N0} 个文件", true, null, null, -1, -1, expand);
}
