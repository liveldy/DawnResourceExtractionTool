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
}

/// <summary>Tree node for the wad list: a folder of the DATA tree, or one wad file.</summary>
public sealed class WadTreeNode : ObservableObject
{
    private bool _isExpanded;

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
            if (args.PropertyName == nameof(WadItem.IsSelected)) OnPropertyChanged(nameof(IsSelected));
        };
    }

    public string Name { get; }

    public string? Detail { get; }

    public WadItem? Wad { get; }

    public bool IsFolder => Wad is null;

    public ObservableCollection<WadTreeNode> Children { get; } = new();

    public int WadCount => (Wad is null ? 0 : 1) + Children.Sum(child => child.WadCount);

    /// <summary>Compact trailing text: wad count for a folder, entry count for a wad.</summary>
    public string CountText => IsFolder ? $"{WadCount}" : $"{Wad!.EntryCount:N0} 条";

    public string DetailToolTip => IsFolder ? $"{WadCount} 个 wad" : Detail ?? string.Empty;

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

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children) child.ExpandAll();
    }
}

/// <summary>Tree node for the asset browser: a category root, a folder or a file leaf.</summary>
public sealed class TreeNodeVm : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    public TreeNodeVm(FileTreeNode node)
    {
        Node = node;
        DisplayName = node.Name;
        Children = new ObservableCollection<TreeNodeVm>(node.Children.Select(child => new TreeNodeVm(child)));

        if (node.IsFolder)
        {
            CountText = node.Category is not null && node.Category.Length == 0
                ? $"{node.FileCount:N0}"
                : $"{node.FileCount:N0} 个 · {ExtractionSummary.Format(node.TotalSize)}";
        }
        else
        {
            CountText = ExtractionSummary.Format(node.Entry?.UncompressedSize ?? 0);
        }
    }

    public FileTreeNode Node { get; }

    public string DisplayName { get; }

    public string CountText { get; }

    public bool IsFolder => Node.IsFolder;

    public WadEntryInfo? Entry => Node.Entry;

    public ObservableCollection<TreeNodeVm> Children { get; }

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
