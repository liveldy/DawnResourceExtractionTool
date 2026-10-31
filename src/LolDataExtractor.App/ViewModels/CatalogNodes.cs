using System.IO;
using System.Collections.ObjectModel;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.ViewModels;

/// <summary>
/// One row of the classified tree: a section, a group, an entity, a folder or a file. Children are
/// built on first expansion — the catalog holds hundreds of thousands of files and must never build
/// them all.
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
        CatalogSection? section,
        int group,
        int entity,
        Func<CatalogNodeVm, Task>? expand,
        string? hint = null,
        string? toolTip = null,
        string? location = null)
    {
        Name = name;
        CountText = countText;
        IsFolder = isFolder;
        Entry = entry;
        Section = section;
        Group = group;
        Entity = entity;
        _expand = expand;
        Hint = hint;
        Location = location;
        ToolTip = toolTip ?? name;
    }

    public string Name { get; }

    /// <summary>Trailing text: file count for a folder, size for a file.</summary>
    public string CountText { get; }

    /// <summary>Small badge next to the name; kept for nodes that need to flag themselves.</summary>
    public string? Hint { get; }

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    /// <summary>Where a search hit sits: "英雄 · Ahri · Base".</summary>
    public string? Location { get; }

    public bool HasLocation => !string.IsNullOrEmpty(Location);

    public string ToolTip { get; }

    public bool IsFolder { get; }

    /// <summary>Set on file rows only.</summary>
    public CatalogEntry? Entry { get; }

    /// <summary>Section this node belongs to; <c>null</c> on the "search results" root.</summary>
    public CatalogSection? Section { get; }

    /// <summary>Index of the group; -1 when the node covers a whole section.</summary>
    public int Group { get; }

    /// <summary>Index of the entity; -1 when the node covers a whole group.</summary>
    public int Entity { get; }

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

    public static CatalogNodeVm ForSection(CatalogSectionInfo info, int files, int groups,
        Func<CatalogNodeVm, Task> expand, string description) =>
        new(info.Display, $"{files:N0} 个文件 · {groups:N0} 个组", true, null, info.Section, -1, -1, expand,
            toolTip: description);

    public static CatalogNodeVm ForGroup(CatalogSection section, int group, string name, string countText,
        string? hint, string? detail, Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, section, group, -1, expand, hint, detail ?? name);

    public static CatalogNodeVm ForEntity(CatalogSection section, int group, int entity, string name,
        string countText, string? detail, Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, section, group, entity, expand, null, detail ?? name);

    public static CatalogNodeVm ForFolder(string name, string countText, Func<CatalogNodeVm, Task> expand) =>
        new(name, countText, true, null, null, -1, -1, expand);

    public static CatalogNodeVm ForFile(CatalogEntry entry, string? location = null) =>
        new(entry.FileName, ExtractionSummary.Format(entry.Size), false, entry, null, -1, -1, null,
            toolTip: entry.Path + (entry.WadCount > 1 ? $"\n（{entry.WadCount} 个 wad 含此条目，打开的是 {Path.GetFileName(entry.WadPath)}）" : string.Empty),
            location: location);

    /// <summary>A plain message row (nothing to show, a truncated list, a search hint).</summary>
    public static CatalogNodeVm Message(string text) =>
        new(text, string.Empty, false, null, null, -1, -1, null);

    public static CatalogNodeVm SearchRoot(string query, int total, Func<CatalogNodeVm, Task> expand) =>
        new($"搜索结果：{query}", $"{total:N0} 个文件", true, null, null, -1, -1, expand);
}
