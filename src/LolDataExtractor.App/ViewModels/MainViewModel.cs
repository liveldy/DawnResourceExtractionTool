using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LolDataExtractor.App.Controls;
using LolDataExtractor.App.Services;
using LolDataExtractor.Core;
using NAudio.Wave;

namespace LolDataExtractor.App.ViewModels;

public sealed record CategoryOption(string Display, string? Value);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Safety net: the tree is not built for more entries than this (search narrows it down).</summary>
    public const int MaxTreeFiles = 20000;

    private readonly AppSettings _settings;
    private readonly Stopwatch _clock = new();

    private NameTable _names = NameTable.Empty;

    /// <summary>Name table of the client archives; merged into <see cref="_names"/> at load time.</summary>
    private NameTable _clientNames = NameTable.Empty;

    private List<WadEntryInfo> _currentEntries = new();

    /// <summary>Hashes ticked in the browser; survives filtering so a search cannot lose them.</summary>
    private readonly HashSet<ulong> _checkedEntries = new();
    private CancellationTokenSource? _cancellation;
    private WadItem? _selectedWad;

    /// <summary>The wad the preview shows; a catalog leaf can live in another wad than the tree.</summary>
    private WadItem? _previewWad;

    /// <summary>Entries of the previewed wad when it is not the one open in the tree.</summary>
    private IReadOnlyList<WadEntryInfo>? _previewEntriesOverride;
    private TreeNodeVm? _selectedNode;
    private string _dataRootText = string.Empty;
    private string _outputRootText = string.Empty;
    private string _hashStatus = "名称索引：未生成";
    private string _searchText = string.Empty;
    private string _wadSearchText = string.Empty;
    private string _statusText = "就绪";
    private string _treeSummary = string.Empty;
    private string _exportPattern = "*";
    private string _exportExtensions = string.Empty;
    private string _threadCount = Math.Clamp(Environment.ProcessorCount - 1, 1, 16).ToString();
    private CategoryOption _selectedCategory;
    private bool _isBusy;
    private bool _optionGroupByWad = true;
    private bool _optionFlat;
    private bool _optionSkipExisting = true;
    private bool _optionIncludeUnnamed = true;
    private bool _optionConvertTextures = true;
    private AudioFormatOption _selectedAudioFormat;
    private double _progressValue;
    private string _selectionSummary = "已选 0 个 wad";
    private bool _allSelected;

    // preview state
    private int _previewToken;
    private WadEntryInfo? _previewEntry;
    private WwiseBank? _previewBank;
    private byte[]? _previewAudioBytes;
    private string _previewAudioExtension = string.Empty;
    private string _previewTitle = string.Empty;
    private string _previewSubtitle = string.Empty;
    private string _previewMessage = string.Empty;
    private ImageSource? _previewImage;
    private Model3D? _previewModel;
    private string? _previewText;
    private string? _previewHex;
    private bool _showImage;
    private bool _showModel;
    private bool _showAudio;
    private bool _showText;
    private bool _showHex;
    private bool _showMessage;
    private AudioTrack? _selectedAudioTrack;
    private string _audioStatus = string.Empty;
    private bool _isAudioPlaying;
    private bool _audioToolMissing;
    private WaveOut? _waveOut;
    private AudioFileReader? _audioReader;
    private int _audioPlaybackToken;

    // speech to text state: notes are edited per stream and transcribed on demand
    private readonly AudioNoteStore _notes;
    private readonly Dictionary<string, Transcriber> _transcribers = new();
    private string _speechStatus = string.Empty;
    private bool _speechModelMissing;
    private bool _isTranscribing;

    // model preview state
    private ModelPreview? _previewModelData;
    private ModelScene? _modelScene;
    private SkeletonData? _previewSkeleton;
    private AnimationClip? _previewClip;
    private string _textureBindingNote = string.Empty;
    private bool _hasModelAssets;
    private bool _isAnimationPlaying;
    private string _animationStatus = string.Empty;
    private DispatcherTimer? _animationTimer;
    private readonly Stopwatch _animationClock = new();
    private AssetChoice? _selectedSkeletonChoice;
    private AssetChoice? _selectedAnimationChoice;
    private AssetChoice? _selectedTextureChoice;
    private readonly ConcurrentDictionary<ulong, SkeletonData> _skeletonCache = new();
    private readonly ConcurrentDictionary<ulong, AnimationClip> _animationCache = new();
    private readonly ConcurrentDictionary<ulong, ImageSource> _textureCache = new();

    // textures that belong to the open model, and the one the auto-pick chose
    private IReadOnlyList<WadEntryInfo> _linkedTextures = Array.Empty<WadEntryInfo>();
    private WadEntryInfo? _autoTextureEntry;

    // export state
    private string _exportPathPrefix = string.Empty;
    private string _selectedExportScope;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _dataRootText = _settings.DataRoot ?? string.Empty;
        _outputRootText = _settings.OutputRoot ?? string.Empty;
        _selectedCategory = Categories[0];
        _selectedExportScope = ExportScopes[0];
        _selectedAudioFormat = AudioFormats[0];
        _audioToolMissing = !VgmstreamTool.IsAvailable;
        _notes = AudioNoteStore.Load();
        _speechModelMissing = !SpeechRecognizer.IsModelPresent;

        foreach (var category in Categories.Where(option => option.Value is not null))
            ExportCategories.Add(new CategoryToggle(category.Value!, category.Display, true));

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsBusy);
        SelectAllCommand = new RelayCommand(ToggleSelectAll);
        SelectNoneCommand = new RelayCommand(() => SetSelection(false));
        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        DownloadHashesCommand = new RelayCommand(async () => await DownloadHashesAsync(), () => !IsBusy);
        OpenOutputCommand = new RelayCommand(OpenOutputFolder);
        BrowseRootCommand = new RelayCommand(BrowseRoot);
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        ClearLogCommand = new RelayCommand(() => Logs.Clear());
        ExportCurrentCommand = new RelayCommand(ExportCurrent, () => _selectedNode?.Entry is not null);
        ExportFolderCommand = new RelayCommand(async () => await ExportFolderAsync(), () => _selectedNode is { IsFolder: true });
        PlayAudioCommand = new RelayCommand(async () => await PlayAudioAsync(), () => IsAudioPlaying == false);
        StopAudioCommand = new RelayCommand(StopAudio, () => IsAudioPlaying);
        DownloadAudioToolCommand = new RelayCommand(async () => await DownloadAudioToolAsync(), () => !IsBusy);
        TranscribeCommand = new RelayCommand(async () => await TranscribeSelectedAsync(),
            () => !IsBusy && !IsTranscribing && SelectedAudioTrack is not null);
        TranscribeAllCommand = new RelayCommand(async () => await TranscribeAllAsync(),
            () => !IsBusy && !IsTranscribing && AudioTracks.Count > 0);
        DownloadSpeechModelCommand = new RelayCommand(async () => await DownloadSpeechModelAsync(), () => !IsBusy && !IsTranscribing);
        CancelTranscribeCommand = new RelayCommand(CancelTranscribe, () => IsTranscribing);
        PlayAnimationCommand = new RelayCommand(StartAnimation, () => !IsAnimationPlaying && _modelScene?.Clip is not null);
        StopAnimationCommand = new RelayCommand(StopAnimation, () => IsAnimationPlaying);
        PickSkeletonCommand = new RelayCommand(() => PickAsset(AssetKind.Skeleton), () => SkeletonChoices.Count > 0);
        PickAnimationCommand = new RelayCommand(() => PickAsset(AssetKind.Animation), () => AnimationChoices.Count > 0);
        PickTextureCommand = new RelayCommand(() => PickAsset(AssetKind.Texture), () => TextureChoices.Count > 0);
        UseCurrentFolderCommand = new RelayCommand(UseCurrentFolder, () => _selectedNode is { IsFolder: true });
        SelectAllCategoriesCommand = new RelayCommand(() => SetCategories(_ => true));
        SelectNoCategoriesCommand = new RelayCommand(() => SetCategories(_ => false));
        SelectCategoryPresetCommand = new RelayCommand(parameter => SetCategories(value => value == parameter as string));
        InitializeCatalog();
        RefreshAudioToolState();
        ResetPreview();
        RaiseCommandStates();
    }

    public static IReadOnlyList<CategoryOption> Categories { get; } = new[]
    {
        new CategoryOption("全部类别", null),
        new CategoryOption("纹理 texture", "texture"),
        new CategoryOption("模型 model", "model"),
        new CategoryOption("音频 audio", "audio"),
        new CategoryOption("数据 bin", "bin"),
        new CategoryOption("文本 text", "text"),
        new CategoryOption("着色器 shader", "shader"),
        new CategoryOption("其他 other", "other"),
    };

    /// <summary>Which wads an export covers.</summary>
    public static IReadOnlyList<string> ExportScopes { get; } =
        new[] { "选择的 wad", "当前 wad", "全部 wad", "当前 wad 选中的内容" };

    /// <summary>How audio entries are written; mp3 first so it is the default.</summary>
    public static IReadOnlyList<AudioFormatOption> AudioFormats { get; } = new[]
    {
        new AudioFormatOption("转成 mp3（推荐）", AudioExportFormat.Mp3),
        new AudioFormatOption("转成 wav（无损，体积大）", AudioExportFormat.Wav),
        new AudioFormatOption("保留原始 wem / bnk", AudioExportFormat.Keep),
    };

    public ObservableCollection<CategoryToggle> ExportCategories { get; } = new();

    public ObservableCollection<WadItem> Wads { get; } = new();

    /// <summary>The wad list as a folder tree of the DATA directory (filtered by the search box).</summary>
    public ObservableCollection<WadTreeNode> WadRoots { get; } = new();

    public ObservableCollection<TreeNodeVm> TreeRoots { get; } = new();

    public ObservableCollection<AudioTrack> AudioTracks { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public RelayCommand ScanCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand DownloadHashesCommand { get; }

    public RelayCommand OpenOutputCommand { get; }

    public RelayCommand BrowseRootCommand { get; }

    public RelayCommand BrowseOutputCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public RelayCommand ExportCurrentCommand { get; }

    public RelayCommand PlayAudioCommand { get; }

    public RelayCommand StopAudioCommand { get; }

    public RelayCommand DownloadAudioToolCommand { get; }

    /// <summary>Transcribes the selected stream into its note.</summary>
    public RelayCommand TranscribeCommand { get; }

    /// <summary>Transcribes every stream of the open bank, one after another.</summary>
    public RelayCommand TranscribeAllCommand { get; }

    public RelayCommand DownloadSpeechModelCommand { get; }

    public RelayCommand CancelTranscribeCommand { get; }

    public RelayCommand ExportFolderCommand { get; }

    public RelayCommand PlayAnimationCommand { get; }

    public RelayCommand StopAnimationCommand { get; }

    public RelayCommand PickSkeletonCommand { get; }

    public RelayCommand PickAnimationCommand { get; }

    public RelayCommand PickTextureCommand { get; }

    public RelayCommand UseCurrentFolderCommand { get; }

    public RelayCommand SelectAllCategoriesCommand { get; }

    public RelayCommand SelectNoCategoriesCommand { get; }

    public RelayCommand SelectCategoryPresetCommand { get; }

    public string DataRootText
    {
        get => _dataRootText;
        set => SetProperty(ref _dataRootText, value);
    }

    public string OutputRootText
    {
        get => _outputRootText;
        set => SetProperty(ref _outputRootText, value);
    }

    public string HashStatus
    {
        get => _hashStatus;
        private set => SetProperty(ref _hashStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string TreeSummary
    {
        get => _treeSummary;
        private set => SetProperty(ref _treeSummary, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    /// <summary>Caption of the first selection button: 全选 while anything is unchecked.</summary>
    public string SelectAllLabel => _allSelected ? "全不选" : "全选";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    /// <summary>Filter of the wad tree; matches the file name or the path below DATA.</summary>
    public string WadSearchText
    {
        get => _wadSearchText;
        set
        {
            if (!SetProperty(ref _wadSearchText, value)) return;
            RebuildWadTree();
            UpdateSelectionSummary();   // keeps the "显示 N 个" part in step with the filter
        }
    }

    public CategoryOption SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value)) ApplyFilter();
        }
    }

    public string ExportPattern
    {
        get => _exportPattern;
        set => SetProperty(ref _exportPattern, value);
    }

    public string ExportExtensions
    {
        get => _exportExtensions;
        set => SetProperty(ref _exportExtensions, value);
    }

    public string ThreadCount
    {
        get => _threadCount;
        set => SetProperty(ref _threadCount, value);
    }

    public bool OptionGroupByWad
    {
        get => _optionGroupByWad;
        set => SetProperty(ref _optionGroupByWad, value);
    }

    public bool OptionFlat
    {
        get => _optionFlat;
        set => SetProperty(ref _optionFlat, value);
    }

    public bool OptionSkipExisting
    {
        get => _optionSkipExisting;
        set => SetProperty(ref _optionSkipExisting, value);
    }

    public bool OptionIncludeUnnamed
    {
        get => _optionIncludeUnnamed;
        set => SetProperty(ref _optionIncludeUnnamed, value);
    }

    /// <summary>Write .tex/.dds as png instead of the raw container.</summary>
    public bool OptionConvertTextures
    {
        get => _optionConvertTextures;
        set => SetProperty(ref _optionConvertTextures, value);
    }

    public AudioFormatOption SelectedAudioFormat
    {
        get => _selectedAudioFormat;
        set => SetProperty(ref _selectedAudioFormat, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public WadItem? SelectedWad
    {
        get => _selectedWad;
        set
        {
            if (ReferenceEquals(_selectedWad, value)) return;
            _selectedWad = value;
            OnPropertyChanged();
            _skeletonCache.Clear();
            _animationCache.Clear();
            _textureCache.Clear();
            _checkedEntries.Clear();   // ticks belong to the wad they were made in
            PendingEntries = LoadEntriesAsync(value);
        }
    }

    /// <summary>The entry load in flight; awaited by the unattended UI self test.</summary>
    public Task? PendingEntries { get; private set; }

    /// <summary>Entries of the wad shown in the tree; the manual asset picker searches them.</summary>
    public IReadOnlyList<WadEntryInfo> CurrentEntries => _currentEntries;

    /// <summary>Currently highlighted tree leaf; drives the preview panel.</summary>
    public TreeNodeVm? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            _previewWad = _selectedWad;   // a tree leaf always belongs to the open wad
            _previewEntriesOverride = null;
            OnPropertyChanged();
            ExportCurrentCommand.RaiseCanExecuteChanged();
            ExportFolderCommand.RaiseCanExecuteChanged();
            UseCurrentFolderCommand.RaiseCanExecuteChanged();
            PendingPreview = LoadPreviewAsync(value);
        }
    }

    /// <summary>The preview load in flight; awaited by the unattended UI self test.</summary>
    public Task? PendingPreview { get; private set; }

    /// <summary>Self test helper: switches to the first wad whose name matches.</summary>
    public async Task<bool> SelectWadAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var wad = Wads.FirstOrDefault(w =>
            w.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
            w.RelativePath.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (wad is null)
        {
            Log($"自检：没有匹配 {name} 的 wad");
            return false;
        }

        SelectedWad = wad;
        if (PendingEntries is { } loading) await loading;
        return true;
    }

    /// <summary>Self test helper: filters the tree by <paramref name="pattern"/> and selects the first entry.</summary>
    public async Task<bool> SelectFirstMatchAsync(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        if (PendingEntries is { } loading) await loading;
        SearchText = pattern;

        // Rebuilding the tree drops the old selection; flush that layout pass first.
        Application.Current?.MainWindow?.UpdateLayout();

        var first = FindFirstLeaf(TreeRoots);
        if (first is null)
        {
            Log($"自检：没有匹配 {pattern} 的条目");
            return false;
        }

        first.IsSelected = true;
        Application.Current?.MainWindow?.UpdateLayout();
        SelectedNode = first;
        if (PendingPreview is { } pending) await pending;
        if (PendingScene is { } scene) await scene;
        return true;
    }

    private static TreeNodeVm? FindFirstLeaf(IEnumerable<TreeNodeVm> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder) return node;
            if (FindFirstLeaf(node.Children) is { } found) return found;
        }
        return null;
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string PreviewSubtitle
    {
        get => _previewSubtitle;
        private set => SetProperty(ref _previewSubtitle, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public Model3D? PreviewModel
    {
        get => _previewModel;
        private set => SetProperty(ref _previewModel, value);
    }

    public string? PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public string? PreviewHex
    {
        get => _previewHex;
        private set => SetProperty(ref _previewHex, value);
    }

    public bool ShowImage
    {
        get => _showImage;
        private set => SetProperty(ref _showImage, value);
    }

    public bool ShowModel
    {
        get => _showModel;
        private set => SetProperty(ref _showModel, value);
    }

    public bool ShowAudio
    {
        get => _showAudio;
        private set => SetProperty(ref _showAudio, value);
    }

    public bool ShowText
    {
        get => _showText;
        private set => SetProperty(ref _showText, value);
    }

    public bool ShowHex
    {
        get => _showHex;
        private set => SetProperty(ref _showHex, value);
    }

    public bool ShowMessage
    {
        get => _showMessage;
        private set => SetProperty(ref _showMessage, value);
    }

    public AudioTrack? SelectedAudioTrack
    {
        get => _selectedAudioTrack;
        set
        {
            if (!SetProperty(ref _selectedAudioTrack, value)) return;
            OnPropertyChanged(nameof(SelectedNote));
            TranscribeCommand.RaiseCanExecuteChanged();
        }
    }

    public string AudioStatus
    {
        get => _audioStatus;
        private set => SetProperty(ref _audioStatus, value);
    }

    /// <summary>Progress of the speech to text run (model download, "3/27 …").</summary>
    public string SpeechStatus
    {
        get => _speechStatus;
        private set => SetProperty(ref _speechStatus, value);
    }

    /// <summary>True while the recognition model has not been downloaded yet.</summary>
    public bool SpeechModelMissing
    {
        get => _speechModelMissing;
        private set => SetProperty(ref _speechModelMissing, value);
    }

    /// <summary>Where the audio notes are stored; shown in the log so the user can open it.</summary>
    public string NotesPath => _notes.Path;

    public int SavedNoteCount => _notes.Count;

    public bool IsTranscribing
    {
        get => _isTranscribing;
        private set
        {
            if (!SetProperty(ref _isTranscribing, value)) return;
            RaiseCommandStates();
        }
    }

    /// <summary>Note of the selected stream, edited in place and saved as it changes.</summary>
    public string SelectedNote
    {
        get => SelectedAudioTrack?.Note ?? string.Empty;
        set
        {
            if (SelectedAudioTrack is not { } track) return;
            track.Note = value;
            OnPropertyChanged();
        }
    }

    public bool IsAudioPlaying
    {
        get => _isAudioPlaying;
        private set
        {
            if (!SetProperty(ref _isAudioPlaying, value)) return;
            PlayAudioCommand.RaiseCanExecuteChanged();
            StopAudioCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>True when vgmstream is missing, so the panel offers to download it.</summary>
    public bool AudioToolMissing
    {
        get => _audioToolMissing;
        private set => SetProperty(ref _audioToolMissing, value);
    }

    public ObservableCollection<AssetChoice> SkeletonChoices { get; } = new();

    public ObservableCollection<AssetChoice> AnimationChoices { get; } = new();

    public ObservableCollection<AssetChoice> TextureChoices { get; } = new();

    /// <summary>True when the model preview has skeleton / clip / texture pickers to show.</summary>
    public bool HasModelAssets
    {
        get => _hasModelAssets;
        private set => SetProperty(ref _hasModelAssets, value);
    }

    public AssetChoice? SelectedSkeletonChoice
    {
        get => _selectedSkeletonChoice;
        set
        {
            if (!SetProperty(ref _selectedSkeletonChoice, value)) return;
            RebuildModelScene();
        }
    }

    public AssetChoice? SelectedAnimationChoice
    {
        get => _selectedAnimationChoice;
        set
        {
            if (!SetProperty(ref _selectedAnimationChoice, value)) return;
            RebuildModelScene();
        }
    }

    public AssetChoice? SelectedTextureChoice
    {
        get => _selectedTextureChoice;
        set
        {
            if (!SetProperty(ref _selectedTextureChoice, value)) return;
            RebuildModelScene();
        }
    }

    public bool IsAnimationPlaying
    {
        get => _isAnimationPlaying;
        private set
        {
            if (!SetProperty(ref _isAnimationPlaying, value)) return;
            PlayAnimationCommand.RaiseCanExecuteChanged();
            StopAnimationCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>What the model page says about the linked skeleton and clip.</summary>
    public string AnimationStatus
    {
        get => _animationStatus;
        private set => SetProperty(ref _animationStatus, value);
    }

    /// <summary>Which texture each group of submeshes got, e.g. "贴图：ahri_base_tx_cm（Body/Eyes）· …".</summary>
    public string TextureBindingNote
    {
        get => _textureBindingNote;
        private set
        {
            if (SetProperty(ref _textureBindingNote, value)) OnPropertyChanged(nameof(HasTextureBinding));
        }
    }

    public bool HasTextureBinding => !string.IsNullOrEmpty(_textureBindingNote);

    public string ExportPathPrefix
    {
        get => _exportPathPrefix;
        set => SetProperty(ref _exportPathPrefix, value);
    }

    public string SelectedExportScope
    {
        get => _selectedExportScope;
        set => SetProperty(ref _selectedExportScope, value);
    }

    /// <summary>Scans the game folder, loads the name index and selects the first wad.</summary>
    public async Task InitializeAsync(string? dataRoot)
    {
        if (!string.IsNullOrWhiteSpace(dataRoot)) DataRootText = dataRoot;

        await RunBusyAsync("读取名称索引…", async () =>
        {
            var game = await Task.Run(() => NameTable.LoadBinaryOrEmpty(_settings.NamesIndexPath));
            _clientNames = await Task.Run(() => NameTable.LoadBinaryOrEmpty(_settings.ClientNamesPath));
            _names = _clientNames.IsEmpty ? game : game.Union(_clientNames);
            HashStatus = _names.IsEmpty
                ? "名称索引：未生成（条目将以哈希命名，点击「下载哈希表」）"
                : $"名称索引：{_names.Count:N0} 条路径";
        });

        if (string.IsNullOrWhiteSpace(DataRootText))
        {
            // Nothing remembered yet: probe the well-known install locations exactly once.
            var found = LolInstall.TryLocate();
            if (found is null)
            {
                StatusText = "未找到游戏目录，请点「浏览」选到 …\\Game\\DATA";
                return;
            }
            DataRootText = found;
        }

        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        var typed = DataRootText?.Trim() ?? string.Empty;

        // What gets scanned is decided by the folder that was picked, never by climbing out of it:
        // the install folder encloses both Game\DATA and LeagueClient\plugins, a DATA folder only
        // the game archives, a plugins folder only the client ones.
        var root = LolInstall.TryLocate(typed);
        var clientRoot = LolClient.TryLocatePlugins(typed);
        if (root is null && clientRoot is null)
        {
            // Never jump to another installation: keep what the user typed and say so.
            StatusText = string.IsNullOrWhiteSpace(typed)
                ? "请先填写游戏 Game\\DATA 目录"
                : $"未能识别游戏目录：{typed}（请选到 …\\Game\\DATA 或安装目录）";
            Log(StatusText);
            return;
        }

        // The box keeps what the user picked: an install folder or a Game folder already means its
        // DATA folder, and every wad of an installation lives there. Rewriting the box made a switch
        // that had worked look like it was rejected, so nothing is normalized into it.
        if (!string.IsNullOrWhiteSpace(typed))
        {
            _settings.DataRoot = Path.IsPathRooted(typed) ? typed : root;
            _settings.Save();
        }

        if (root is not null && !string.Equals(typed.Trim('"'), root, StringComparison.OrdinalIgnoreCase))
            Log($"游戏目录：{typed} → {root}");

        await RunBusyAsync("扫描 wad 文件…", async () =>
        {
            var progress = new Progress<WadSummary>(summary =>
            {
                var item = new WadItem(summary);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(WadItem.IsSelected)) UpdateSelectionSummary();
                };
                Wads.Add(item);
                UpdateSelectionSummary();
            });

            Wads.Clear();
            TreeRoots.Clear();
            _currentEntries = new List<WadEntryInfo>();
            SelectedNode = null;
            UpdateSelectionSummary();

            var timer = Stopwatch.StartNew();
            var summaries = await Task.Run(() =>
            {
                var list = root is null ? new List<WadSummary>() : WadCatalog.Scan(root, progress).ToList();
                if (clientRoot is not null) list.AddRange(WadCatalog.ScanClient(clientRoot, progress));
                return list;
            });
            timer.Stop();

            var clientWads = summaries.Count(s => s.IsClient);
            var gameWads = summaries.Count - clientWads;
            var scope = (gameWads, clientWads) switch
            {
                (> 0, > 0) => $"{gameWads} 个游戏 wad + {clientWads} 个客户端 wad",
                (0, > 0) => $"{clientWads} 个客户端 wad",
                _ => $"{gameWads} 个 wad",
            };
            var entries = summaries.Sum(s => s.EntryCount);
            StatusText = $"共 {scope}，{entries:N0} 个条目，用时 {timer.Elapsed.TotalSeconds:F1}s";
            Log($"扫描完成：共 {scope}，{entries:N0} 个条目");

            RebuildWadTree();
            if (Wads.Count > 0) SelectedWad = Wads.OrderByDescending(w => w.EntryCount).First();
            if (PendingEntries is { } loading) await loading;
        });
    }

    private async Task LoadEntriesAsync(WadItem? wad)
    {
        SelectedNode = null;

        if (wad is null)
        {
            _currentEntries = new List<WadEntryInfo>();
            TreeRoots.Clear();
            TreeSummary = string.Empty;
            return;
        }

        var path = wad.FullPath;
        var names = _names;
        StatusText = $"读取 {wad.Name} …";

        var entries = await Task.Run(() =>
        {
            using var archive = WadArchive.Open(path);
            return archive.ReadEntries(names).ToList();
        });

        if (!ReferenceEquals(_selectedWad, wad)) return;

        _currentEntries = entries;
        ApplyFilter();

        var named = entries.Count(e => e.IsNamed);
        StatusText = $"{wad.Name}：{entries.Count:N0} 条目，已命名 {named:N0}（{100.0 * named / Math.Max(1, entries.Count):F1}%）";
    }

    /// <summary>Rebuilds the tree from the current search and category filters.</summary>
    private void ApplyFilter()
    {
        // Keep the ticks the user made before the rebuild: the tree is thrown away and rebuilt.
        SyncCheckedEntries();

        var query = (IEnumerable<WadEntryInfo>)_currentEntries;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim().ToLowerInvariant();
            query = query.Where(e => (e.Path ?? PathHashing.ToText(e.Hash)).ToLowerInvariant().Contains(needle));
        }

        if (SelectedCategory?.Value is { } category)
            query = query.Where(e => e.Category == category);

        var filtered = query.ToList();
        var total = _currentEntries.Count;
        var truncated = filtered.Count > MaxTreeFiles;
        if (truncated) filtered = filtered.Take(MaxTreeFiles).ToList();

        var roots = FileTree.Build(filtered);
        var expandAll = filtered.Count <= 300;

        TreeRoots.Clear();
        foreach (var root in roots)
        {
            var node = new TreeNodeVm(root, _checkedEntries) { IsExpanded = true };
            if (expandAll) node.ExpandAll();
            else ExpandChain(node);
            TreeRoots.Add(node);
        }

        TreeSummary = total == 0
            ? string.Empty
            : truncated
                ? $"显示前 {filtered.Count:N0} 条 / 共 {total:N0} 条（用搜索缩小范围）"
                : $"共 {total:N0} 条";
    }

    /// <summary>Opens single-child folder chains so the tree starts where it actually branches.</summary>
    private static void ExpandChain(TreeNodeVm node)
    {
        while (true)
        {
            if (node.Children.Count != 1) return;
            var child = node.Children[0];
            if (!child.IsFolder) return;
            child.IsExpanded = true;
            node = child;
        }
    }

    /// <summary>
    /// Ticks every wad the tree currently shows. With a search active that is the filtered set only,
    /// so a quick "select all" never picks up wads the user has filtered away.
    /// </summary>
    private void ToggleSelectAll()
    {
        var visible = VisibleWads();
        var select = !visible.All(wad => wad.IsSelected);
        foreach (var wad in visible) wad.IsSelected = select;
        UpdateSelectionSummary();
    }

    /// <summary>The wads the tree shows under the current search.</summary>
    private List<WadItem> VisibleWads() => MatchingWads(WadSearchText.Trim()).ToList();

    private IEnumerable<WadItem> MatchingWads(string needle) =>
        string.IsNullOrEmpty(needle)
            ? Wads
            : Wads.Where(wad => wad.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || wad.RelativePath.Contains(needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Rebuilds the wad tree from the current search: folders of the DATA directory on the way
    /// down, one leaf per wad. A search keeps only matching wads and expands the way to them.
    /// </summary>
    private void RebuildWadTree()
    {
        var needle = WadSearchText.Trim();
        var matches = MatchingWads(needle).ToList();

        var roots = new ObservableCollection<WadTreeNode>();
        foreach (var wad in matches.OrderBy(wad => wad.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var segments = wad.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            AddWadNode(roots, null, segments, 0, wad);
        }

        WadRoots.Clear();
        foreach (var root in roots.OrderBy(node => node.IsFolder ? 0 : 1)
                     .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
        {
            root.ExpandAll(AutoExpandWads);
            WadRoots.Add(root);
        }
    }

    /// <summary>
    /// Folders up to this many wads open themselves. The game tree holds one folder with 576 wads,
    /// which used to bury the client assets below a wall of rows.
    /// </summary>
    private const int AutoExpandWads = 20;

    /// <summary>Walks one folder level of <paramref name="segments"/> and hangs the wad leaf on the end.</summary>
    private static void AddWadNode(ObservableCollection<WadTreeNode> level, WadTreeNode? parent,
        string[] segments, int index, WadItem wad)
    {
        if (index >= segments.Length - 1)
        {
            var leaf = new WadTreeNode(wad) { Parent = parent };
            level.Add(leaf);
            return;
        }

        var folder = level.FirstOrDefault(node => node.IsFolder && node.Name == segments[index]);
        if (folder is null)
        {
            folder = new WadTreeNode(segments[index]) { Parent = parent };
            level.Add(folder);
        }

        AddWadNode(folder.Children, folder, segments, index + 1, wad);
    }

    /// <summary>Clears the ticks of the wads the tree currently shows (the filtered set, if searching).</summary>
    private void SetSelection(bool selected)
    {
        foreach (var wad in VisibleWads()) wad.IsSelected = selected;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var visible = VisibleWads();
        var selected = Wads.Count(w => w.IsSelected);
        _allSelected = visible.Count > 0 && visible.All(w => w.IsSelected);
        SelectionSummary = visible.Count == Wads.Count
            ? $"已选 {selected} 个 wad / 共 {Wads.Count} 个"
            : $"已选 {selected} / 显示 {visible.Count} / 共 {Wads.Count} 个";
        OnPropertyChanged(nameof(SelectAllLabel));
    }

    private async Task LoadPreviewAsync(TreeNodeVm? node)
    {
        if (node?.Entry is not { } entry || _previewWad is not { } wad)
        {
            ++_previewToken;
            ResetPreview();
            return;
        }

        await PreviewEntryAsync(wad, entry);
    }

    /// <summary>Shows one entry of one wad; the tree and the catalog both end up here.</summary>
    private async Task PreviewEntryAsync(WadItem wad, WadEntryInfo entry)
    {
        var token = ++_previewToken;
        ResetPreview();

        BeginPreview(entry);
        var result = await ReadPreviewAsync(wad, entry);
        if (token != _previewToken) return;

        // A .bnk that only carries events keeps its streams in the sibling .wpk.
        if (result.Kind == PreviewKind.Message && IsVoiceHint(entry) && FindVoicePackage(entry) is { } voice)
        {
            BeginPreview(voice);
            result = await ReadPreviewAsync(wad, voice);
            if (token != _previewToken) return;
            Log($"{entry.FileName} 里没有音频流，已改用同名的 {voice.FileName}");
        }

        ApplyPreview(result);

        if (result.Kind == PreviewKind.Model && result.Model is { } model)
        {
            LinkModelAssets(model);
            PendingScene = BuildModelSceneAsync(token);
        }
    }

    private static bool IsVoiceHint(WadEntryInfo entry)
        => string.Equals(entry.Extension, "bnk", StringComparison.OrdinalIgnoreCase);

    private void BeginPreview(WadEntryInfo entry)
    {
        _previewEntry = entry;
        PreviewTitle = entry.IsNamed ? entry.FileName : PathHashing.ToText(entry.Hash);
        PreviewSubtitle = $"{FileTree.CategoryDisplay(entry.Category)} · {ExtractionSummary.Format(entry.UncompressedSize)}";
        PreviewAudiotrackReset();
    }

    private static async Task<PreviewResult> ReadPreviewAsync(WadItem wad, WadEntryInfo entry)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var archive = WadArchive.Open(wad.FullPath);
                using var stream = archive.OpenDecompressed(entry.Hash);
                return PreviewService.Load(stream, entry.UncompressedSize, entry);
            });
        }
        catch (Exception ex)
        {
            return PreviewResult.Failed(string.Empty, "读取条目失败：" + ex.Message);
        }
    }

    /// <summary>The <c>.wpk</c> next to a <c>.bnk</c> that holds the actual voice streams.</summary>
    private WadEntryInfo? FindVoicePackage(WadEntryInfo entry)
    {
        var directory = DirectoryOf(entry.Path ?? string.Empty);
        var stem = Path.GetFileNameWithoutExtension(entry.FileName);

        return PreviewEntries.FirstOrDefault(candidate =>
            string.Equals(candidate.Extension, "wpk", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileNameWithoutExtension(candidate.FileName), stem, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DirectoryOf(candidate.Path ?? string.Empty), directory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The scene rebuild in flight; awaited by the unattended UI self test.</summary>
    public Task? PendingScene { get; private set; }

    private void PreviewAudiotrackReset()
    {
        _previewBank = null;
        _previewAudioBytes = null;
        _previewAudioExtension = string.Empty;
        AudioTracks.Clear();
        SelectedAudioTrack = null;
        AudioStatus = string.Empty;
    }

    private void ApplyPreview(PreviewResult result)
    {
        if (!string.IsNullOrEmpty(result.Summary)) PreviewSubtitle = $"{PreviewSubtitle} · {result.Summary}";

        // The placeholder from ResetPreview only survives when nothing could be rendered.
        ShowMessage = result.Kind == PreviewKind.Message;
        PreviewMessage = ShowMessage ? result.Message ?? "暂不支持预览这种文件" : string.Empty;

        switch (result.Kind)
        {
            case PreviewKind.Image:
            {
                PreviewImage = CreateImage(result);
                ShowImage = PreviewImage is not null;
                if (!ShowImage)
                {
                    PreviewMessage = result.Message ?? "无法显示这张图片";
                    ShowMessage = true;
                }
                break;
            }

            case PreviewKind.Model:
            {
                if (result.Model is { } model)
                {
                    // Show the static mesh right away; the linked skeleton, clip and texture
                    // arrive a moment later from BuildModelSceneAsync.
                    _previewModelData = model;
                    PreviewModel = ModelGeometryBuilder.Build(model);
                    ShowModel = PreviewModel is not null;
                }
                if (!ShowModel)
                {
                    PreviewMessage = result.Message ?? "无法构建 3D 视图";
                    PreviewHex = result.HexDump;
                    ShowHex = result.HexDump is not null;
                    ShowMessage = true;
                }
                break;
            }

            case PreviewKind.Audio:
            {
                ShowAudio = true;
                _previewAudioExtension = _previewEntry?.Extension ?? string.Empty;

                if (result.Bank is { } bank)
                {
                    _previewBank = bank;
                    LoadAudioTracks(bank);
                    AudioStatus = AudioToolMissing
                        ? "音频包已解析；播放与转写需要先下载 vgmstream 解码工具"
                        : "选择一条音频流后点「播放」或「转换」";
                }
                else if (result.RawAudio is { } raw)
                {
                    _previewAudioBytes = raw;
                    LoadAudioTracks(null);
                    AudioStatus = AudioToolMissing && _previewAudioExtension != "wav"
                        ? "播放与转写需要先下载 vgmstream 解码工具"
                        : "点「播放」或「转换」";
                }
                else
                {
                    PreviewMessage = result.Message ?? "无法读取音频";
                    ShowMessage = true;
                }
                break;
            }

            case PreviewKind.Text:
            {
                // Client tables are JSON: they get the formatted, searchable, click-through view.
                var isJson = string.Equals(_previewEntry?.Extension, "json", StringComparison.OrdinalIgnoreCase);
                PreviewJson = isJson ? result.Text : null;
                ShowJson = isJson && result.Text is not null;
                PreviewText = isJson ? null : result.Text;
                ShowText = !ShowJson && result.Text is not null;
                if (!ShowText && !ShowJson)
                {
                    PreviewMessage = result.Message ?? "内容为空";
                    ShowMessage = true;
                }
                break;
            }

            case PreviewKind.Hex:
            {
                PreviewHex = result.HexDump;
                ShowHex = result.HexDump is not null;
                PreviewMessage = result.Message ?? "二进制数据，未识别出可预览的格式";
                ShowMessage = true;
                break;
            }

            default:
            {
                PreviewMessage = result.Message ?? "暂不支持预览这种文件";
                PreviewHex = result.HexDump;
                ShowHex = result.HexDump is not null;
                ShowMessage = true;
                break;
            }
        }
    }

    private static ImageSource? CreateImage(PreviewResult result)
    {
        if (result.RawImage is { } raw)
        {
            using var stream = new MemoryStream(raw, writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }

        if (result.Image is not { } image) return null;
        if (image.Rgba.Length < image.Width * image.Height * 4) return null;

        // WPF has no RGBA32 format: swap to BGRA.
        var pixels = new byte[image.Width * image.Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = image.Rgba[i + 2];
            pixels[i + 1] = image.Rgba[i + 1];
            pixels[i + 2] = image.Rgba[i];
            pixels[i + 3] = image.Rgba[i + 3];
        }

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Entries of the wad the preview shows: the open wad, or the catalog one.</summary>
    public IReadOnlyList<WadEntryInfo> PreviewEntries => _previewEntriesOverride ?? _currentEntries;

    private void ResetPreview()
    {
        ShowImage = ShowModel = ShowAudio = ShowText = ShowHex = ShowMessage = false;
        ShowJson = false;
        PreviewJson = null;
        PreviewImage = null;
        PreviewModel = null;
        PreviewText = null;
        PreviewHex = null;
        PreviewMessage = string.Empty;
        PreviewTitle = string.Empty;
        PreviewSubtitle = string.Empty;
        PreviewAudiotrackReset();
        ResetModelAssets();
        StopAudio();

        // Nothing selected: say what the panel is for instead of showing an empty box.
        PreviewMessage = "在中间的目录树里点一个文件即可预览：贴图 / 3D 模型 / 音频 / 文本";
        ShowMessage = true;
    }

    // ---------------------------------------------------------------- model assets

    /// <summary>Clears the skeleton / clip / texture pickers and stops playback.</summary>
    private void ResetModelAssets()
    {
        StopAnimation();
        _previewModelData = null;
        _modelScene = null;
        _previewSkeleton = null;
        _previewClip = null;
        TextureBindingNote = string.Empty;
        _selectedSkeletonChoice = null;
        _selectedAnimationChoice = null;
        _selectedTextureChoice = null;
        _linkedTextures = Array.Empty<WadEntryInfo>();
        _autoTextureEntry = null;
        SkeletonChoices.Clear();
        AnimationChoices.Clear();
        TextureChoices.Clear();
        HasModelAssets = false;
        AnimationStatus = string.Empty;
        OnPropertyChanged(nameof(SelectedSkeletonChoice));
        OnPropertyChanged(nameof(SelectedAnimationChoice));
        OnPropertyChanged(nameof(SelectedTextureChoice));
        RaiseAssetCommandStates();
    }

    /// <summary>Fills the pickers with the assets that belong to the model, best guess selected.</summary>
    private void LinkModelAssets(ModelPreview model)
    {
        _previewModelData = model;
        SkeletonChoices.Clear();
        AnimationChoices.Clear();
        TextureChoices.Clear();

        if (_previewEntry is not { } entry || PreviewEntries.Count == 0)
        {
            HasModelAssets = false;
            RaiseAssetCommandStates();
            return;
        }

        var linked = AssetLinker.Find(entry, PreviewEntries);
        FillChoices(SkeletonChoices, linked.Skeletons, "无骨骼（静态显示）");
        FillChoices(AnimationChoices, linked.Animations, "无动画（绑定姿势）");
        FillChoices(TextureChoices, linked.Textures, "无贴图（纯色）");

        _selectedSkeletonChoice = FindChoice(SkeletonChoices, linked.DefaultSkeleton);
        _selectedAnimationChoice = FindChoice(AnimationChoices, linked.DefaultAnimation);
        _selectedTextureChoice = FindChoice(TextureChoices, linked.DefaultTexture);
        _linkedTextures = linked.Textures;
        _autoTextureEntry = _selectedTextureChoice?.Entry;
        OnPropertyChanged(nameof(SelectedSkeletonChoice));
        OnPropertyChanged(nameof(SelectedAnimationChoice));
        OnPropertyChanged(nameof(SelectedTextureChoice));

        HasModelAssets = SkeletonChoices.Count > 1 || AnimationChoices.Count > 1 || TextureChoices.Count > 1;
        AnimationStatus = linked.Describe;
        RaiseAssetCommandStates();
    }

    private static void FillChoices(ObservableCollection<AssetChoice> target, IReadOnlyList<WadEntryInfo> entries, string noneLabel)
    {
        foreach (var entry in entries)
            target.Add(new AssetChoice(entry.FileName, ExtractionSummary.Format(entry.UncompressedSize), entry));
        target.Add(new AssetChoice(noneLabel, string.Empty, null));
    }

    /// <summary>The choice for the automatic guess, or the first entry when the guess is empty.</summary>
    private static AssetChoice FindChoice(ObservableCollection<AssetChoice> choices, WadEntryInfo? preferred)
        => choices.FirstOrDefault(choice => preferred is not null && choice.Entry == preferred) ?? choices[0];

    private void RaiseAssetCommandStates()
    {
        PickSkeletonCommand.RaiseCanExecuteChanged();
        PickAnimationCommand.RaiseCanExecuteChanged();
        PickTextureCommand.RaiseCanExecuteChanged();
        PlayAnimationCommand.RaiseCanExecuteChanged();
        StopAnimationCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reloads the scene after the user picked another skeleton, clip or texture.</summary>
    private void RebuildModelScene()
    {
        if (_previewModelData is null) return;
        StopAnimation();
        PendingScene = BuildModelSceneAsync(_previewToken);
    }

    private sealed record AssetBundle(SkeletonData? Skeleton, AnimationClip? Clip,
        IReadOnlyList<ImageSource?> Textures, string? TextureSummary, string? Error);

    private async Task BuildModelSceneAsync(int token)
    {
        var model = _previewModelData;
        var wad = _previewWad;
        if (model is null || wad is null) return;

        var skeletonEntry = _selectedSkeletonChoice?.Entry;
        var animationEntry = _selectedAnimationChoice?.Entry;
        var textureEntry = _selectedTextureChoice?.Entry;
        var wadPath = wad.FullPath;

        AssetBundle loaded;
        try
        {
            loaded = await Task.Run(() => LoadModelAssets(model, wadPath, skeletonEntry, animationEntry, textureEntry));
        }
        catch (Exception ex)
        {
            loaded = new AssetBundle(null, null, Array.Empty<ImageSource?>(), null, ex.Message);
        }

        if (token != _previewToken || !ReferenceEquals(_previewModelData, model)) return;

        _previewSkeleton = loaded.Skeleton;
        _previewClip = loaded.Clip;
        TextureBindingNote = loaded.TextureSummary ?? string.Empty;

        var scene = ModelScene.Create(model, loaded.Skeleton, loaded.Clip, loaded.Textures);
        if (scene is null)
        {
            _modelScene = null;
            PreviewModel = ModelGeometryBuilder.Build(model, loaded.Textures);
        }
        else
        {
            _modelScene = scene;
            PreviewModel = scene.Group;
        }

        UpdateAnimationStatus(loaded.Error);
    }

    /// <summary>Reads and decodes the selected assets; runs off the UI thread.</summary>
    private AssetBundle LoadModelAssets(ModelPreview model, string wadPath, WadEntryInfo? skeletonEntry,
        WadEntryInfo? animationEntry, WadEntryInfo? textureEntry)
    {
        string? error = null;
        SkeletonData? skeleton = null;
        AnimationClip? clip = null;
        string? note = null;
        var textures = new ImageSource?[model.Parts.Count];

        WadArchive? archive = null;
        try
        {
            if (skeletonEntry is not null || animationEntry is not null || textureEntry is not null)
                archive = WadArchive.Open(wadPath);

            if (skeletonEntry is not null && archive is not null)
            {
                skeleton = _skeletonCache.GetOrAdd(skeletonEntry.Hash,
                    _ => SkeletonDecoder.Decode(ReadEntry(archive, skeletonEntry.Hash), skeletonEntry.FileName));
            }

            if (animationEntry is not null && archive is not null)
            {
                clip = _animationCache.GetOrAdd(animationEntry.Hash,
                    _ => AnimationDecoder.Decode(ReadEntry(archive, animationEntry.Hash), animationEntry.FileName));
            }

            if (textureEntry is not null && archive is not null)
            {
                var plan = BuildTexturePlan(model, textureEntry);
                note = DescribeTexturePlan(model, plan);
                for (var i = 0; i < plan.Length && i < textures.Length; i++)
                {
                    if (plan[i] is not { } entry) continue;
                    textures[i] = LoadTexture(archive, entry);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            archive?.Dispose();
        }

        return new AssetBundle(skeleton, clip, textures, note, error);
    }

    /// <summary>
    /// Chooses the texture of every submesh: the auto-picked skin texture is split over the
    /// parts by name (<c>Tails</c> → <c>ahri_base_tails_tx_cm</c>), while a texture the user
    /// picked by hand is used for the whole model.
    /// </summary>
    private WadEntryInfo?[] BuildTexturePlan(ModelPreview model, WadEntryInfo textureEntry)
    {
        var plan = new WadEntryInfo?[model.Parts.Count];
        var manual = _autoTextureEntry is not null && textureEntry != _autoTextureEntry;
        if (manual || _linkedTextures.Count == 0)
        {
            Array.Fill(plan, textureEntry);
            return plan;
        }

        var stems = _linkedTextures.Select(entry => System.IO.Path.GetFileNameWithoutExtension(entry.FileName)).ToArray();
        var assignment = TextureBinder.Assign(model.Parts.Select(part => part.Name).ToArray(), stems,
            System.IO.Path.GetFileNameWithoutExtension(_previewEntry?.FileName ?? string.Empty));

        // A negative index means the folder holds no colour map at all (only masks or the
        // loading screen), so that submesh stays untextured instead of showing a data map.
        for (var i = 0; i < plan.Length; i++)
            plan[i] = assignment[i] >= 0 && assignment[i] < _linkedTextures.Count ? _linkedTextures[assignment[i]] : null;

        return plan;
    }

    private ImageSource LoadTexture(WadArchive archive, WadEntryInfo entry)
        => _textureCache.GetOrAdd(entry.Hash, _ =>
        {
            var result = PreviewService.LoadImage(ReadEntry(archive, entry.Hash), entry.Extension);
            return CreateImage(result)!;
        });

    /// <summary>"贴图：ahri_base_tx_cm（Body/Eyes）· ahri_base_tails_tx_cm（Tails/Tail_Large）".</summary>
    private static string? DescribeTexturePlan(ModelPreview model, IReadOnlyList<WadEntryInfo?> plan)
    {
        var groups = new List<(string Name, List<string> Parts)>();
        for (var i = 0; i < plan.Count && i < model.Parts.Count; i++)
        {
            if (plan[i] is not { } entry) continue;
            var group = groups.FirstOrDefault(item => item.Name == entry.FileName);
            if (group.Name is null)
            {
                group = (entry.FileName, new List<string>());
                groups.Add(group);
            }

            group.Parts.Add(model.Parts[i].Name);
        }

        if (groups.Count == 0) return "贴图：没有可用的颜色贴图（该文件夹只有遮罩 / 加载图）";
        if (groups.Count == 1) return "贴图：" + groups[0].Name;

        return "贴图：" + string.Join(" · ", groups.Select(group =>
            $"{Path.GetFileNameWithoutExtension(group.Name)}（{string.Join("/", group.Parts.Take(4))}）"));
    }

    private static byte[] ReadEntry(WadArchive archive, ulong hash)
    {
        using var stream = archive.OpenDecompressed(hash);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void UpdateAnimationStatus(string? error)
    {
        RaiseAssetCommandStates();

        if (!string.IsNullOrEmpty(error))
        {
            AnimationStatus = "加载资源失败：" + error;
            Log("模型资源加载失败：" + error);
            return;
        }

        if (_previewSkeleton is null)
        {
            AnimationStatus = "未找到骨骼文件，按静态模型显示（可手动选择 .skl）";
            return;
        }

        AnimationStatus = _modelScene?.Clip is { } clip
            ? $"{clip.Name} · {clip.Duration:F1}s · {clip.Fps:F0}fps · {clip.Tracks.Count} 条关节轨道"
            : $"骨骼 {_previewSkeleton.JointCount} 个关节 · 未播放动画，显示绑定姿势";
    }

    private void StartAnimation()
    {
        if (_modelScene?.Clip is not { } clip) return;

        _animationClock.Restart();
        AnimationTimer.Start();
        IsAnimationPlaying = true;
        AnimationStatus = $"{clip.Name} · {clip.Duration:F1}s · {clip.Fps:F0}fps · 播放中";
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        IsAnimationPlaying = false;
    }

    private DispatcherTimer AnimationTimer => _animationTimer ??= CreateAnimationTimer();

    private DispatcherTimer CreateAnimationTimer()
    {
        // ~30 fps: one pose per displayed frame is plenty and keeps the CPU busy work small.
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            if (_modelScene?.Clip is not { } clip)
            {
                StopAnimation();
                return;
            }

            var elapsed = (float)_animationClock.Elapsed.TotalSeconds;
            _modelScene.Apply(elapsed);
            AnimationStatus = $"{clip.Name} · {elapsed % clip.Duration:F2}s / {clip.Duration:F1}s · {clip.Fps:F0}fps";
        };
        return timer;
    }

    private enum AssetKind
    {
        Skeleton,
        Animation,
        Texture,
    }

    /// <summary>Lets the user pick a skeleton, clip or texture outside the automatic guess.</summary>
    private void PickAsset(AssetKind kind)
    {
        if (_selectedWad is null) return;

        var extension = kind switch
        {
            AssetKind.Skeleton => "skl",
            AssetKind.Animation => "anm",
            _ => "tex",
        };
        var title = kind switch
        {
            AssetKind.Skeleton => "选择骨骼文件（.skl）",
            AssetKind.Animation => "选择动画文件（.anm）",
            _ => "选择贴图（.tex）",
        };

        var picker = new EntryPickerWindow(PreviewEntries, title, extension)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (picker.ShowDialog() != true || picker.SelectedEntry is not { } entry) return;

        var collection = kind switch
        {
            AssetKind.Skeleton => SkeletonChoices,
            AssetKind.Animation => AnimationChoices,
            _ => TextureChoices,
        };

        var choice = collection.FirstOrDefault(item => item.Entry == entry);
        if (choice is null)
        {
            choice = new AssetChoice(entry.FileName, ExtractionSummary.Format(entry.UncompressedSize), entry);
            var noneIndex = collection.Count - 1;
            collection.Insert(noneIndex < 0 ? 0 : noneIndex, choice);
        }

        switch (kind)
        {
            case AssetKind.Skeleton:
                SelectedSkeletonChoice = choice;
                break;
            case AssetKind.Animation:
                SelectedAnimationChoice = choice;
                break;
            default:
                SelectedTextureChoice = choice;
                break;
        }

        var label = kind switch
        {
            AssetKind.Skeleton => "骨骼",
            AssetKind.Animation => "动画",
            _ => "贴图",
        };
        Log($"手动选择{label}：{entry.Path}");
    }

    /// <summary>导出当前选择；界面按钮与自检脚本共用同一入口。</summary>
    public Task ExportAsync() => ExportAsync(null, null);

    /// <summary>Wads covered by the current export scope.</summary>
    private List<string> ResolveExportWads()
    {
        if (SelectedExportScope == ExportScopes[1] || SelectedExportScope == ExportScopes[3])
            return SelectedWad is null ? new List<string>() : new List<string> { SelectedWad.FullPath };

        if (SelectedExportScope == ExportScopes[2])
            return Wads.Select(wad => wad.FullPath).ToList();

        var selected = Wads.Where(wad => wad.IsSelected).Select(wad => wad.FullPath).ToList();
        if (selected.Count == 0 && SelectedWad is not null) selected.Add(SelectedWad.FullPath);
        return selected;
    }

    /// <summary>True for the scope that exports what the tree of the open wad has ticked.</summary>
    private bool IsSelectionScope => SelectedExportScope == ExportScopes[3];

    /// <summary>Copies the tick state of the current tree into the checked set, unticks included.</summary>
    private void SyncCheckedEntries()
    {
        foreach (var root in TreeRoots) root.SyncChecked(_checkedEntries);
    }

    /// <summary>Self test helper: ticks every entry the tree currently shows.</summary>
    public void CheckAllEntries()
    {
        foreach (var root in TreeRoots) root.CheckState = true;
        SyncCheckedEntries();
        Log($"自检：勾选目录树里全部条目 → {_checkedEntries.Count:N0} 个");
    }

    /// <summary>
    /// Narrows a filter to the entries ticked in the open wad's tree. The manual path pattern,
    /// folder prefix and extension fields step aside because the ticks are the more specific
    /// instruction; category and media options still apply.
    /// </summary>
    private bool RestrictToSelection(ExtractionFilter filter)
    {
        if (_selectedWad is null)
        {
            StatusText = "请先打开要导出的 wad";
            return false;
        }

        SyncCheckedEntries();
        if (_checkedEntries.Count == 0)
        {
            StatusText = "请先在目录树里勾选要导出的条目";
            return false;
        }

        filter.PathPrefix = null;
        filter.PathPattern = null;
        filter.Extensions = null;
        filter.ExactPaths = null;
        filter.ExactHashes = new HashSet<ulong>(_checkedEntries);
        return true;
    }

    /// <summary>Checked categories, or <c>null</c> when every category is exported.</summary>
    private HashSet<string>? SelectedExportCategories()
    {
        var values = ExportCategories
            .Where(toggle => toggle.IsChecked)
            .Select(toggle => toggle.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return values.Count == 0 || values.Count == ExportCategories.Count ? null : values;
    }

    /// <summary>
    /// 导出：<paramref name="wadsOverride"/> 与 <paramref name="categoryOverride"/> 供目录导出使用。
    /// </summary>
    private async Task ExportAsync(IReadOnlyCollection<string>? wadsOverride = null, string? categoryOverride = null)
    {
        var selected = wadsOverride?.ToList() ?? ResolveExportWads();
        if (selected.Count == 0)
        {
            StatusText = "没有可导出的 wad：请勾选 wad，或把导出范围改成「当前 wad」/「全部 wad」";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputRootText))
        {
            StatusText = "请先设置导出目录";
            return;
        }

        var categories = categoryOverride is null
            ? SelectedExportCategories()
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { categoryOverride };

        var filter = new ExtractionFilter
        {
            PathPattern = NormalizePattern(ExportPattern),
            PathPrefix = string.IsNullOrWhiteSpace(ExportPathPrefix) ? null : ExportPathPrefix.Trim(),
            Extensions = string.IsNullOrWhiteSpace(ExportExtensions)
                ? null
                : ExportExtensions
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.TrimStart('.').ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
            Categories = categories,
            IncludeUnnamed = OptionIncludeUnnamed,
        };

        if (wadsOverride is null && IsSelectionScope && !RestrictToSelection(filter)) return;

        var options = new ExtractionOptions
        {
            OutputRoot = Path.GetFullPath(OutputRootText),
            GroupByWad = OptionGroupByWad,
            FlatLayout = OptionFlat,
            SkipExisting = OptionSkipExisting,
            Threads = int.TryParse(ThreadCount, out var threads) ? Math.Clamp(threads, 1, 64) : 8,
            ConvertTextures = OptionConvertTextures,
            AudioFormat = SelectedAudioFormat.Value,
        };

        _settings.OutputRoot = options.OutputRoot;
        _settings.Save();

        Log($"导出开始：{selected.Count} 个 wad · 范围「{SelectedExportScope}」" +
            (filter.PathPrefix is null ? string.Empty : $" · 目录 {filter.PathPrefix}") +
            (filter.ExactPaths is { Count: > 0 } exact ? $" · 条目 {string.Join("、", exact)}" : string.Empty) +
            (filter.ExactHashes is { Count: > 0 } hashes ? $" · 勾选 {hashes.Count:N0} 个条目" : string.Empty) +
            (categories is null ? string.Empty : $" · 类别 {string.Join(",", categories)}"));

        await RunBusyAsync("导出中…", async () =>
        {
            var progress = new Progress<ExtractionProgress>(p =>
            {
                ProgressValue = p.FilesTotal == 0 ? 0 : p.FilesDone * 100.0 / p.FilesTotal;
                StatusText = $"导出 {p.FilesDone:N0}/{p.FilesTotal:N0} · 成功 {p.Extracted:N0} · 跳过 {p.Skipped:N0} · {ExtractionSummary.Format(p.BytesDone)}";
            });

            var root = LolInstall.TryLocate(DataRootText) ?? string.Empty;
            var summary = await Task.Run(() =>
                ExtractionService.Extract(root, selected, _names, filter, options, progress, null, _cancellation!.Token));

            ProgressValue = 100;
            StatusText = summary.Describe();
            Log(summary.ManifestPath is null ? summary.Describe() : $"{summary.Describe()}；清单 {Path.GetFileName(summary.ManifestPath)}");
            foreach (var error in summary.Errors.Take(5)) Log("失败：" + error);
        });
    }

    /// <summary>导出目录树里选中的目录：把它作为路径前缀导出当前 wad 的这一部分。</summary>
    private async Task ExportFolderAsync()
    {
        if (_selectedNode is not { IsFolder: true } node || _selectedWad is not { } wad)
        {
            StatusText = "请先在目录树里选中一个目录";
            return;
        }

        if (string.IsNullOrWhiteSpace(node.Node.Path))
        {
            StatusText = "类别根不能单独导出，请选中具体目录，或用「导出类别」勾选";
            return;
        }

        ExportPathPrefix = node.Node.Path;
        ExportPattern = "*";
        await ExportAsync(new[] { wad.FullPath }, node.Node.Category);
    }

    /// <summary>把选中目录的路径填进「目录前缀」，方便按位置导出。</summary>
    private void UseCurrentFolder()
    {
        if (_selectedNode is not { } node) return;

        var path = node.IsFolder ? node.Node.Path : DirectoryOf(node.Node.Path);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "选中项没有具体目录路径";
            return;
        }

        ExportPathPrefix = path;
        StatusText = "导出目录前缀：" + path;
    }

    private static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? string.Empty : path[..slash];
    }

    private void SetCategories(Func<string, bool> select)
    {
        foreach (var toggle in ExportCategories) toggle.IsChecked = select(toggle.Value);
    }

    /// <summary>Writes the entry currently shown in the preview to the export folder.</summary>
    private void ExportCurrent()
    {
        if (_selectedNode?.Entry is not { } entry || _previewWad is not { } wad) return;
        if (string.IsNullOrWhiteSpace(OutputRootText))
        {
            StatusText = "请先设置导出目录";
            return;
        }

        var wadPath = wad.FullPath;
        var output = OutputRootText;
        var token = _previewToken;
        var media = new ExtractionOptions
        {
            OutputRoot = Path.GetFullPath(output),
            ConvertTextures = OptionConvertTextures,
            AudioFormat = SelectedAudioFormat.Value,
        };

        // A bank or voice package exports the stream picked in the audio list, not all of them.
        var streamIndex = MediaExporter.IsConvertibleAudio(entry.Extension) && AudioTracks.Count > 1
            ? SelectedAudioTrack?.Index
            : null;

        _ = Task.Run(() =>
        {
            try
            {
                var relative = entry.IsNamed
                    ? ExtractionService.NormalizeAssetPath(entry.Path!)
                    : Path.Combine("_unnamed", PathHashing.ToText(entry.Hash) + "." + (entry.Extension.Length == 0 ? "bin" : entry.Extension));
                var destination = SafePath.Fit(Path.GetFullPath(output), ExtractionService.WadKeyOf(wadPath), relative);

                using var archive = WadArchive.Open(wadPath);
                var files = MediaExporter.Export(archive, entry, destination, media, audioStream: streamIndex);

                if (token != _previewToken) return;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (files.Count == 0)
                    {
                        StatusText = "没有可导出的内容";
                        Log("导出当前文件：没有可导出的内容");
                        return;
                    }

                    var what = streamIndex is { } index ? $"（第 {index + 1} 条流）" : string.Empty;
                    StatusText = "已导出到 " + files[0];
                    Log($"导出当前文件{what}：" + string.Join("、", files));
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusText = "导出失败：" + ex.Message;
                    Log("导出失败：" + ex.Message);
                });
            }
        });
    }

    private async Task PlayAudioAsync()
    {
        if (_previewEntry is not { } entry || _previewWad is not { } wad) return;

        StopAudio();
        var playback = ++_audioPlaybackToken;
        AudioStatus = "准备音频…";

        var streamIndex = _previewBank is not null ? (SelectedAudioTrack?.Index ?? 0) : -1;

        try
        {
            var wadPath = wad.FullPath;
            var rawAudio = _previewAudioBytes;
            var bank = _previewBank;
            var extension = _previewAudioExtension;

            var wavPath = await Task.Run(() => PrepareAudioFile(wadPath, entry, bank, rawAudio, extension, streamIndex));
            if (playback != _audioPlaybackToken) return;

            var reader = new AudioFileReader(wavPath);
            var output = new WaveOut();
            output.PlaybackStopped += (_, args) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (playback != _audioPlaybackToken) return;
                    IsAudioPlaying = false;
                    AudioStatus = args.Exception is null ? "播放结束" : "播放失败：" + args.Exception.Message;
                });
            };

            _audioReader = reader;
            _waveOut = output;
            output.Init(reader);
            output.Play();

            IsAudioPlaying = true;
            AudioStatus = $"正在播放（{reader.TotalTime:mm\\:ss}）";
        }
        catch (Exception ex)
        {
            IsAudioPlaying = false;
            AudioStatus = "试听失败：" + ex.Message;
            Log("试听失败：" + ex.Message);
        }
    }

    /// <summary>Fills the audio list; each row carries the note saved for its stream.</summary>
    private void LoadAudioTracks(WwiseBank? bank)
    {
        AudioTracks.Clear();
        SpeechStatus = string.Empty;

        if (bank is null)
        {
            // A bare wem / wav is a single stream of its own.
            if (_previewEntry is { } entry && _previewWad is { } wad)
                AddAudioTrack(new WwiseSound(0, 0, 0, 0, 0), wad.FullPath, entry.Hash);
            SelectedAudioTrack = AudioTracks.FirstOrDefault();
            return;
        }

        if (_previewEntry is not { } current || _previewWad is not { } currentWad) return;
        foreach (var sound in bank.Sounds) AddAudioTrack(sound, currentWad.FullPath, current.Hash);
        SelectedAudioTrack = AudioTracks.FirstOrDefault();
    }

    private void AddAudioTrack(WwiseSound sound, string wadPath, ulong entryHash)
    {
        var key = AudioNoteStore.KeyOf(wadPath, entryHash, sound.Index);
        var track = new AudioTrack(sound, key) { Note = _notes.Get(key) ?? string.Empty };
        track.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AudioTrack.Note)) _notes.Set(track.Key, track.Note);
        };
        AudioTracks.Add(track);
    }

    /// <summary>Transcribes the selected stream into its note.</summary>
    private async Task TranscribeSelectedAsync()
    {
        if (SelectedAudioTrack is not { } track || _previewEntry is not { } entry || _previewWad is not { } wad) return;

        var token = _cancellation = new CancellationTokenSource();
        IsTranscribing = true;
        try
        {
            SpeechStatus = "准备语音识别…";
            var text = await TranscribeTrackAsync(track, wad, entry, token.Token);
            if (token.IsCancellationRequested) return;

            ApplyTranscript(track, text);
            SpeechStatus = text.Length == 0 ? "没有识别出文字（可能是音效而非语音）" : "已转写并写入备注";
        }
        catch (OperationCanceledException)
        {
            SpeechStatus = "已停止转写";
        }
        catch (Exception ex)
        {
            SpeechStatus = "转写失败：" + ex.Message;
            Log("转写失败：" + ex.Message);
        }
        finally
        {
            IsTranscribing = false;
            RaiseCommandStates();
        }
    }

    /// <summary>Transcribes every stream of the open bank that has no note yet.</summary>
    private async Task TranscribeAllAsync()
    {
        if (AudioTracks.Count == 0 || _previewEntry is not { } entry || _previewWad is not { } wad) return;

        var tracks = AudioTracks.ToList();
        var token = _cancellation = new CancellationTokenSource();
        IsTranscribing = true;
        var done = 0;
        var filled = 0;
        var skipped = 0;

        try
        {
            foreach (var track in tracks)
            {
                if (token.IsCancellationRequested) break;

                // A stream that already has a note (typed or transcribed before) is left alone.
                if (track.HasNote)
                {
                    skipped++;
                    done++;
                    continue;
                }

                SpeechStatus = $"批量转写 {done + 1}/{tracks.Count}…";
                try
                {
                    var text = await TranscribeTrackAsync(track, wad, entry, token.Token);
                    if (text.Length > 0)
                    {
                        ApplyTranscript(track, text);
                        filled++;
                    }
                    else
                    {
                        track.Note = string.Empty;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"转写 #{track.Index + 1} 失败：{ex.Message}");
                }
                done++;
            }

            SpeechStatus = token.IsCancellationRequested
                ? $"已停止：完成 {done}/{tracks.Count}"
                : $"批量转写完成：{tracks.Count} 条，新转写 {filled} 条，已有备注 {skipped} 条跳过";
        }
        finally
        {
            IsTranscribing = false;
            RaiseCommandStates();
        }
    }

    private void ApplyTranscript(AudioTrack track, string text)
    {
        track.Note = text;
        _notes.Set(track.Key, text);
        if (ReferenceEquals(track, SelectedAudioTrack)) OnPropertyChanged(nameof(SelectedNote));
    }

    /// <summary>Decodes the stream to wav and runs whisper over it.</summary>
    private async Task<string> TranscribeTrackAsync(AudioTrack track, WadItem wad, WadEntryInfo entry, CancellationToken token)
    {
        if (AudioToolMissing) await DownloadAudioToolAsync().ConfigureAwait(true);

        var wavPath = await Task.Run(
            () => PrepareAudioFile(wad.FullPath, entry, _previewBank, _previewAudioBytes, _previewAudioExtension, track.Index),
            token);

        var transcriber = await GetTranscriberAsync(wad.FullPath, token).ConfigureAwait(true);
        return await transcriber.TranscribeAsync(wavPath, token).ConfigureAwait(true);
    }

    /// <summary>One whisper context per language; the model is downloaded on first use.</summary>
    private async Task<Transcriber> GetTranscriberAsync(string wadPath, CancellationToken token)
    {
        var language = SpeechRecognizer.LanguageOf(Path.GetFileName(wadPath));
        if (_transcribers.TryGetValue(language, out var cached)) return cached;

        if (!SpeechRecognizer.IsModelPresent)
        {
            var progress = new Progress<string>(message => SpeechStatus = message);
            await SpeechRecognizer.EnsureModelAsync(progress, token).ConfigureAwait(true);
            SpeechModelMissing = false;
        }

        SpeechStatus = "正在加载语音识别模型…";
        var transcriber = await Task.Run(() => Transcriber.Create(language), token).ConfigureAwait(true);
        _transcribers[language] = transcriber;
        return transcriber;
    }

    private async Task DownloadSpeechModelAsync()
    {
        try
        {
            var progress = new Progress<string>(message => SpeechStatus = message);
            await SpeechRecognizer.EnsureModelAsync(progress, CancellationToken.None);
            SpeechModelMissing = false;
            SpeechStatus = "语音识别模型已就绪";
        }
        catch (Exception ex)
        {
            SpeechStatus = "模型下载失败：" + ex.Message;
            Log("语音识别模型下载失败：" + ex.Message);
        }
    }

    private void CancelTranscribe()
    {
        _cancellation?.Cancel();
        SpeechStatus = "正在停止…";
    }

    /// <summary>Produces a playable wav file for the selected entry, transcoding when needed.</summary>
    private static string PrepareAudioFile(string wadPath, WadEntryInfo entry, WwiseBank? bank, byte[]? rawAudio,
        string extension, int streamIndex)
    {
        Directory.CreateDirectory(VgmstreamTool.CacheDirectory);
        var key = PathHashing.ToText(entry.Hash);

        if (bank is not null)
        {
            // A wpk is a plain container of wems: extract the one stream and decode it.
            if (bank.Format == "wpk")
            {
                var streamPath = Path.Combine(VgmstreamTool.CacheDirectory, $"{key}-{streamIndex + 1}.wem");
                if (!File.Exists(streamPath)) bank.WriteSound(streamIndex, streamPath);

                var wemTarget = Path.Combine(VgmstreamTool.CacheDirectory, $"{key}-{streamIndex + 1}.wav");
                if (File.Exists(wemTarget) && new FileInfo(wemTarget).Length > 44) return wemTarget;
                return VgmstreamTool.ConvertToWav(streamPath, null, wemTarget);
            }

            var bankPath = Path.Combine(VgmstreamTool.CacheDirectory, key + ".bnk");
            if (!File.Exists(bankPath))
            {
                using var archive = WadArchive.Open(wadPath);
                archive.CopyEntryTo(entry.Hash, bankPath, overwrite: true);
            }

            var wavPath = Path.Combine(VgmstreamTool.CacheDirectory, $"{key}-{streamIndex + 1}.wav");
            if (File.Exists(wavPath) && new FileInfo(wavPath).Length > 44) return wavPath;
            return VgmstreamTool.ConvertToWav(bankPath, streamIndex + 1, wavPath);
        }

        if (rawAudio is null) throw new InvalidOperationException("没有可播放的音频数据");

        if (extension == "wav")
        {
            var wavPath = Path.Combine(VgmstreamTool.CacheDirectory, key + ".wav");
            if (!File.Exists(wavPath)) File.WriteAllBytes(wavPath, rawAudio);
            return wavPath;
        }

        var wemPath = Path.Combine(VgmstreamTool.CacheDirectory, key + "." + (extension.Length == 0 ? "wem" : extension));
        if (!File.Exists(wemPath)) File.WriteAllBytes(wemPath, rawAudio);

        var target = Path.Combine(VgmstreamTool.CacheDirectory, key + "-raw.wav");
        if (File.Exists(target) && new FileInfo(target).Length > 44) return target;
        return VgmstreamTool.ConvertToWav(wemPath, null, target);
    }

    private void StopAudio()
    {
        _audioPlaybackToken++;
        try
        {
            _waveOut?.Stop();
        }
        catch (Exception)
        {
            // Stopping an already finished device is not an error.
        }

        _waveOut?.Dispose();
        _waveOut = null;
        _audioReader?.Dispose();
        _audioReader = null;
        IsAudioPlaying = false;
    }

    private void RefreshAudioToolState()
    {
        AudioToolMissing = !VgmstreamTool.IsAvailable;
    }

    private async Task DownloadAudioToolAsync()
    {
        await RunBusyAsync("下载音频解码工具…", async () =>
        {
            var progress = new Progress<string>(message =>
            {
                StatusText = message;
                AudioStatus = message;
            });

            var executable = await VgmstreamTool.EnsureAsync(progress, _cancellation!.Token);
            RefreshAudioToolState();
            AudioStatus = "音频解码工具就绪，可以试听";
            StatusText = "音频解码工具就绪";
            Log("vgmstream 就绪：" + executable);
        });
    }

    private async Task DownloadHashesAsync()
    {
        await RunBusyAsync("下载哈希表…", async () =>
        {
            var fetcher = new HashTableFetcher();
            var progress = new Progress<HashTableFetchProgress>(p =>
            {
                ProgressValue = p.BytesTotal == 0 ? 0 : p.BytesDone * 100.0 / p.BytesTotal;
                StatusText = $"哈希表 {p.FilesDone}/{p.FilesTotal} 个分片 · {ExtractionSummary.Format(p.BytesDone)} / {ExtractionSummary.Format(p.BytesTotal)}";
            });

            var result = await fetcher.FetchAsync(_settings.HashTableDirectory, token: null, progress: progress);
            Log($"哈希表就绪：下载 {result.Downloaded} 个、跳过 {result.Skipped} 个，共 {ExtractionSummary.Format(result.Bytes)}");

            StatusText = "生成名称索引…";
            var textFiles = HashTableFetcher.FindTextTables(_settings.HashTableDirectory);
            // Never overwrite a working index with an empty one: a directory the fetch did not
            // fill (or a stale one) used to produce a 20 byte index and hash-only names.
            if (textFiles.Count == 0)
                throw new InvalidOperationException($"哈希表目录中没有文本文件，索引未改动：{_settings.HashTableDirectory}");
            var table = await Task.Run(() => NameTable.LoadTextFiles(textFiles));

            if (Wads.Count > 0)
            {
                var hashes = await Task.Run(() =>
                {
                    var set = new HashSet<ulong>();
                    foreach (var wad in Wads.Where(w => !w.IsClient))
                    {
                        using var archive = WadArchive.Open(wad.FullPath);
                        foreach (var entry in archive.ReadEntries(null)) set.Add(entry.Hash);
                    }
                    return set;
                });
                var scoped = await Task.Run(() => table.Filter(hashes));
                Log($"按本地 {hashes.Count:N0} 个 hash 收敛索引：命中 {scoped.Count:N0} 条");
                table = scoped;
            }

            await Task.Run(() => table.SaveBinary(_settings.NamesIndexPath));
            Log($"名称索引已写入 {_settings.NamesIndexPath}（{ExtractionSummary.Format(new FileInfo(_settings.NamesIndexPath).Length)}）");

            // The client archives have their own name space; its table is a single 17 MB file.
            NameTable clientTable;
            try
            {
                StatusText = "下载客户端哈希表…";
                var clientText = await fetcher.FetchClientTableAsync(_settings.ClientTableDirectory, token: null, progress: progress);
                clientTable = await Task.Run(() => NameTable.LoadTextFiles(new[] { clientText }));
                await Task.Run(() => clientTable.SaveBinary(_settings.ClientNamesPath));
                Log($"客户端名称索引已写入 {_settings.ClientNamesPath}（{clientTable.Count:N0} 条）");
            }
            catch (Exception ex)
            {
                clientTable = _clientNames;
                Log("客户端哈希表未更新：" + ex.Message);
            }

            _clientNames = clientTable;
            _names = clientTable.IsEmpty ? table : table.Union(clientTable);
            HashStatus = $"名称索引：{_names.Count:N0} 条路径";

            if (SelectedWad is not null) await LoadEntriesAsync(SelectedWad);
        });
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        StatusText = "正在取消…";
    }

    private void OpenOutputFolder()
    {
        var folder = OutputRootText;
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void BrowseRoot()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择英雄联盟 Game\\DATA 目录" };
        if (Directory.Exists(DataRootText)) dialog.InitialDirectory = DataRootText;
        if (dialog.ShowDialog() == true)
        {
            DataRootText = dialog.FolderName;
            _ = ScanAsync();
        }
    }

    private void BrowseOutput()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择导出目录" };
        if (Directory.Exists(OutputRootText)) dialog.InitialDirectory = OutputRootText;
        if (dialog.ShowDialog() == true) OutputRootText = dialog.FolderName;
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        if (IsBusy) return;

        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        ProgressValue = 0;
        StatusText = status;
        _clock.Restart();
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
            Log("操作已取消");
        }
        catch (Exception ex)
        {
            StatusText = "出错：" + ex.Message;
            Log("出错：" + ex);
            MessageBox.Show(ex.Message, "英雄联盟数据提取工具", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _clock.Stop();
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        DownloadHashesCommand.RaiseCanExecuteChanged();
        DownloadAudioToolCommand.RaiseCanExecuteChanged();
        ExportCurrentCommand.RaiseCanExecuteChanged();
        ExportFolderCommand.RaiseCanExecuteChanged();
        UseCurrentFolderCommand.RaiseCanExecuteChanged();
        PlayAudioCommand.RaiseCanExecuteChanged();
        StopAudioCommand.RaiseCanExecuteChanged();
        TranscribeCommand.RaiseCanExecuteChanged();
        TranscribeAllCommand.RaiseCanExecuteChanged();
        CancelTranscribeCommand.RaiseCanExecuteChanged();
        DownloadSpeechModelCommand.RaiseCanExecuteChanged();
        RaiseAssetCommandStates();
    }

    public void Log(string message)
    {
        // A background progress callback must not write the bound list itself: queue it on the UI
        // thread and keep the messages in the order they were produced.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Log(message));
            return;
        }

        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 500) Logs.RemoveAt(0);
    }

    private static string? NormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var trimmed = pattern.Trim();
        return trimmed.Contains('*') || trimmed.Contains('?') ? trimmed : $"*{trimmed}*";
    }

    public void Dispose()
    {
        StopAudio();
        StopAnimation();
        if (_animationTimer is not null)
        {
            _animationTimer.Stop();
            _animationTimer = null;
        }
    }
}
