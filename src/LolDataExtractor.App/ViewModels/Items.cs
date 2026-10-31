using System.Collections.ObjectModel;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.ViewModels;

public sealed class WadItem : ObservableObject
{
    private bool _isSelected;

    public WadItem(WadSummary summary)
    {
        Summary = summary;
    }

    public WadSummary Summary { get; }

    public string FullPath => Summary.FullPath;

    public string Name => Summary.Name;

    public string RelativePath => Summary.RelativePath;

    public int EntryCount => Summary.EntryCount;

    public string SizeText => ExtractionSummary.Format(Summary.FileSize);

    public string Detail => $"{EntryCount:N0} 条目 · {ExtractionSummary.Format(Summary.UncompressedSize)} · {SizeText}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>True for the client plugin archives below <c>LeagueClient\plugins</c>.</summary>
    public bool IsClient => Summary.IsClient;
}

/// <summary>Tree node for the wad list: a folder of the DATA tree, or one wad file.</summary>
public sealed class WadTreeNode : ObservableObject
{
    private bool _isExpanded;
    private bool? _checkState;

    public WadTreeNode(string name)
    {
        Name = name;
    }

    public WadTreeNode(WadItem wad)
    {
        Wad = wad;
        Name = wad.Name;
        Detail = wad.Detail;

        // Selecting every wad from the toolbar must also tick the boxes in the tree.
        wad.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(WadItem.IsSelected)) return;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(CheckState));
            Parent?.RefreshCheckState();
        };
    }

    public string Name { get; }

    public string? Detail { get; }

    public WadItem? Wad { get; }

    public bool IsFolder => Wad is null;

    /// <summary>Enclosing folder, so a tick can bubble up and keep the folder box in step.</summary>
    public WadTreeNode? Parent { get; set; }

    public ObservableCollection<WadTreeNode> Children { get; } = new();

    public int WadCount => (Wad is null ? 0 : 1) + Children.Sum(child => child.WadCount);

    /// <summary>How many wads in this subtree are ticked.</summary>
    public int SelectedWadCount =>
        (Wad?.IsSelected == true ? 1 : 0) + Children.Sum(child => child.SelectedWadCount);

    /// <summary>Compact trailing text: wad count for a folder, entry count for a wad.</summary>
    public string CountText => IsFolder ? $"{WadCount}" : $"{Wad!.EntryCount:N0} 条";

    public string DetailToolTip => IsFolder ? $"{WadCount} 个 wad" : Detail ?? string.Empty;

    /// <summary>Hover text of the tick box: folders tick everything below them.</summary>
    public string? CheckToolTip => IsFolder ? $"勾选或取消这个目录下的全部 {WadCount} 个 wad" : null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Checkbox state of a wad leaf; folders have no checkbox of their own.</summary>
    public bool IsSelected
    {
        get => Wad?.IsSelected ?? false;
        set
        {
            if (Wad is null || Wad.IsSelected == value) return;
            Wad.IsSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Bound to the tick box of both leaves and folders. A leaf follows its wad; a folder shows
    /// "all selected / none / some" (three-state display) and ticking it selects its whole subtree.
    /// </summary>
    public bool? CheckState
    {
        get => IsFolder ? _checkState ?? ComputeCheckState() : Wad?.IsSelected ?? false;
        set
        {
            if (!IsFolder)
            {
                if (Wad is null || Wad.IsSelected == (value == true)) return;
                Wad.IsSelected = value == true;
                return;
            }

            // A half-ticked folder only ever reports null; the click itself must pick a direction.
            if (value is null || WadCount == 0) return;
            SetSelectedRecursive(value.Value);
            Parent?.RefreshCheckState();
        }
    }

    private bool? ComputeCheckState()
    {
        if (!IsFolder) return Wad?.IsSelected ?? false;
        var total = WadCount;
        if (total == 0) return false;
        var selected = SelectedWadCount;
        return selected == 0 ? false : selected == total ? true : null;
    }

    /// <summary>Applies one tick state to every wad below this node.</summary>
    private void SetSelectedRecursive(bool selected)
    {
        foreach (var child in Children)
        {
            if (child.IsFolder)
            {
                child.SetSelectedRecursive(selected);
            }
            else if (child.Wad is { } wad && wad.IsSelected != selected)
            {
                wad.IsSelected = selected;
            }
        }

        RefreshCheckState();
    }

    /// <summary>Recomputes a folder's tick state after a descendant changed, walking up to the root.</summary>
    public void RefreshCheckState()
    {
        if (!IsFolder) return;
        var state = ComputeCheckState();
        if (_checkState == state) return;
        _checkState = state;
        OnPropertyChanged(nameof(CheckState));
        Parent?.RefreshCheckState();
    }

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children) child.ExpandAll();
    }

    /// <summary>
    /// Expands this folder and its children while the subtree stays small. The game tree holds one
    /// folder with 576 wads, which used to push the client assets out of sight below a wall of rows.
    /// </summary>
    public void ExpandAll(int maxWads)
    {
        IsExpanded = WadCount <= maxWads;
        foreach (var child in Children) child.ExpandAll(maxWads);
    }
}

/// <summary>Tree node for the asset browser: a category root, a folder or a file leaf.</summary>
public sealed class TreeNodeVm : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isChecked;
    private bool? _checkState;

    public TreeNodeVm(FileTreeNode node, IReadOnlySet<ulong>? checkedHashes = null)
        : this(node, null, checkedHashes)
    {
    }

    private TreeNodeVm(FileTreeNode node, TreeNodeVm? parent, IReadOnlySet<ulong>? checkedHashes)
    {
        Node = node;
        Parent = parent;
        DisplayName = node.Name;
        Children = new ObservableCollection<TreeNodeVm>(
            node.Children.Select(child => new TreeNodeVm(child, this, checkedHashes)));

        if (node.IsFolder)
        {
            CountText = node.Category is not null && node.Category.Length == 0
                ? $"{node.FileCount:N0}"
                : $"{node.FileCount:N0} 个 · {ExtractionSummary.Format(node.TotalSize)}";
        }
        else
        {
            CountText = ExtractionSummary.Format(node.Entry?.UncompressedSize ?? 0);
            _isChecked = node.Entry is { } entry && checkedHashes is not null && checkedHashes.Contains(entry.Hash);
        }
    }

    public FileTreeNode Node { get; }

    public string DisplayName { get; }

    public string CountText { get; }

    public bool IsFolder => Node.IsFolder;

    public WadEntryInfo? Entry => Node.Entry;

    /// <summary>Enclosing folder, so a tick can bubble up and keep the folder box in step.</summary>
    public TreeNodeVm? Parent { get; }

    public ObservableCollection<TreeNodeVm> Children { get; }

    /// <summary>Files at or below this node, counted on the nodes actually in the tree.</summary>
    public int TotalFiles => IsFolder ? Children.Sum(child => child.TotalFiles) : 1;

    /// <summary>Ticked files at or below this node.</summary>
    public int CheckedFiles => IsFolder ? Children.Sum(child => child.CheckedFiles) : (_isChecked ? 1 : 0);

    /// <summary>Hover text of the tick box: folders tick everything below them.</summary>
    public string? CheckToolTip => IsFolder ? $"勾选或取消这个目录下的全部 {TotalFiles:N0} 个条目" : null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Tick box state: a file follows its own flag, a folder shows all / none / some and ticking it
    /// marks every file below it.
    /// </summary>
    public bool? CheckState
    {
        get => IsFolder ? _checkState ?? ComputeCheckState() : _isChecked;
        set
        {
            if (!IsFolder)
            {
                var wanted = value == true;
                if (_isChecked == wanted) return;
                _isChecked = wanted;
                OnPropertyChanged(nameof(CheckState));
                Parent?.RefreshCheckState();
                return;
            }

            if (value is null || TotalFiles == 0) return;
            SetCheckedRecursive(value.Value);
            Parent?.RefreshCheckState();
        }
    }

    private bool? ComputeCheckState()
    {
        if (!IsFolder) return _isChecked;
        var total = TotalFiles;
        if (total == 0) return false;
        var ticked = CheckedFiles;
        return ticked == 0 ? false : ticked == total ? true : null;
    }

    private void SetCheckedRecursive(bool value)
    {
        if (!IsFolder)
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged(nameof(CheckState));
            return;
        }

        foreach (var child in Children) child.SetCheckedRecursive(value);
        RefreshCheckState();
    }

    /// <summary>Recomputes a folder's tick state after a child changed, walking up to the root.</summary>
    public void RefreshCheckState()
    {
        if (!IsFolder) return;
        var state = ComputeCheckState();
        if (_checkState == state) return;
        _checkState = state;
        OnPropertyChanged(nameof(CheckState));
        Parent?.RefreshCheckState();
    }

    /// <summary>Collects the hashes of every ticked file at or below this node.</summary>
    public void CollectChecked(ICollection<ulong> hashes)
    {
        if (!IsFolder)
        {
            if (_isChecked && Entry is { } entry) hashes.Add(entry.Hash);
            return;
        }

        foreach (var child in Children) child.CollectChecked(hashes);
    }

    /// <summary>Adds ticked files to <paramref name="hashes"/> and drops the ones unticked.</summary>
    public void SyncChecked(ICollection<ulong> hashes)
    {
        if (!IsFolder)
        {
            if (Entry is not { } entry) return;
            if (_isChecked) hashes.Add(entry.Hash);
            else hashes.Remove(entry.Hash);
            return;
        }

        foreach (var child in Children) child.SyncChecked(hashes);
    }

    /// <summary>Expands this node and everything below it (used after a filtered rebuild).</summary>
    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
        {
            if (child.IsFolder) child.ExpandAll();
        }
    }
}

/// <summary>One playable stream of a sound bank, listed in the audio preview.</summary>
public sealed class AudioTrack : ObservableObject
{
    private string _note = string.Empty;
    private bool _isTranscribing;

    public AudioTrack(WwiseSound sound, string key)
    {
        Index = sound.Index;
        Id = sound.Id;
        Codec = sound.Codec;
        Key = key;
        Title = $"#{sound.Index + 1:D2}  {sound.Label}";
        Detail = $"{ExtractionSummary.Format(sound.Length)} · {sound.Codec}";
    }

    public int Index { get; }

    public uint Id { get; }

    public string Codec { get; }

    public string Title { get; }

    public string Detail { get; }

    /// <summary>Stable id of this stream, used to look its note up again after a restart.</summary>
    public string Key { get; }

    /// <summary>Editable text: what was transcribed, or what the user typed.</summary>
    public string Note
    {
        get => _note;
        set
        {
            if (!SetProperty(ref _note, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(HasNote));
            OnPropertyChanged(nameof(NotePreview));
        }
    }

    /// <summary>First line of the note, shortened for the list.</summary>
    public string NotePreview
    {
        get
        {
            var note = string.Join(' ', _note.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return note.Length <= 46 ? note : note[..46] + "…";
        }
    }

    public bool HasNote => _note.Length > 0;

    public bool IsTranscribing
    {
        get => _isTranscribing;
        set => SetProperty(ref _isTranscribing, value);
    }
}

/// <summary>A skeleton, clip or texture offered for the model preview; <c>null</c> means "none".</summary>
public sealed class AssetChoice
{
    public AssetChoice(string title, string detail, WadEntryInfo? entry)
    {
        Title = title;
        Detail = detail;
        Entry = entry;
    }

    public string Title { get; }

    public string Detail { get; }

    public WadEntryInfo? Entry { get; }

    public bool IsNone => Entry is null;

    public override string ToString() => Title;
}

/// <summary>One export category with its own checkbox.</summary>
public sealed class CategoryToggle : ObservableObject
{
    private bool _isChecked;

    public CategoryToggle(string value, string display, bool isChecked)
    {
        Value = value;
        Display = display;
        _isChecked = isChecked;
    }

    public string Value { get; }

    public string Display { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>One choice of the audio export format combo box.</summary>
public sealed record AudioFormatOption(string Title, AudioExportFormat Value)
{
    public override string ToString() => Title;
}
