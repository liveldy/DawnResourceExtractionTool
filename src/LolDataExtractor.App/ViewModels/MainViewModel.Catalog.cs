using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.ViewModels;

/// <summary>
/// The classified browser: category → hero → skin → folder → file, built from every wad at once.
/// Also the home of the manual safety net — a right click turns the selected node into a rule that
/// survives restarts.
/// </summary>
public sealed partial class MainViewModel
{
    private AssetCatalog? _catalog;
    private readonly Dictionary<string, IReadOnlyList<WadEntryInfo>> _previewEntryCache = new(StringComparer.OrdinalIgnoreCase);
    private CatalogNodeVm? _selectedCatalogNode;
    private string _catalogSearchText = string.Empty;
    private string _catalogStatus = "分类索引：尚未建立";
    private bool _catalogMode;
    private bool _catalogBusy;
    private string _catalogAssignName = string.Empty;

    public ObservableCollection<CatalogNodeVm> CatalogRoots { get; } = new();

    /// <summary>Result of the catalog search, shown instead of the tree while a query is active.</summary>
    public ObservableCollection<CatalogNodeVm> CatalogSearchRoots { get; } = new();

    /// <summary>True when the middle pane shows the classified tree instead of one wad.</summary>
    public bool CatalogMode
    {
        get => _catalogMode;
        set
        {
            if (!SetProperty(ref _catalogMode, value)) return;
            OnPropertyChanged(nameof(WadMode));
            if (value && _catalog is null) PendingCatalogBuild = BuildCatalogAsync();
        }
    }

    /// <summary>Inverse of <see cref="CatalogMode"/>, for the two way switch binding.</summary>
    public bool WadMode
    {
        get => !_catalogMode;
        set => CatalogMode = !value;
    }

    /// <summary>Values of the "浏览" box: one wad at a time, or the classified tree.</summary>
    public static IReadOnlyList<string> BrowseModes { get; } = new[] { WadBrowse, CatalogBrowse };

    private const string WadBrowse = "按 wad 浏览";

    private const string CatalogBrowse = "按分类浏览";

    public string BrowseMode
    {
        get => CatalogMode ? CatalogBrowse : WadBrowse;
        set
        {
            var catalog = string.Equals(value, CatalogBrowse, StringComparison.Ordinal);
            if (catalog == CatalogMode) return;
            CatalogMode = catalog;
            OnPropertyChanged();
        }
    }

    /// <summary>True while the catalog search has hits; the tree is replaced by the result list.</summary>
    public bool CatalogShowingSearch => CatalogSearchRoots.Count > 0;

    public bool CatalogTreeVisible => !CatalogShowingSearch;

    public string CatalogStatus
    {
        get => _catalogStatus;
        private set => SetProperty(ref _catalogStatus, value);
    }

    /// <summary>Name typed into the "归属到英雄" box; an alias, a Chinese name or a free label.</summary>
    public string CatalogAssignName
    {
        get => _catalogAssignName;
        set => SetProperty(ref _catalogAssignName, value);
    }

    /// <summary>Search over every path of the catalog (not just the open wad).</summary>
    public string CatalogSearchText
    {
        get => _catalogSearchText;
        set
        {
            if (!SetProperty(ref _catalogSearchText, value)) return;
            _catalogSearchTimer?.Stop();
            _catalogSearchTimer ??= CreateCatalogSearchTimer();
            _catalogSearchTimer.Start();
        }
    }

    private DispatcherTimer? _catalogSearchTimer;

    private DispatcherTimer CreateCatalogSearchTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyCatalogSearch();
        };
        return timer;
    }

    public CatalogNodeVm? SelectedCatalogNode
    {
        get => _selectedCatalogNode;
        set
        {
            if (ReferenceEquals(_selectedCatalogNode, value)) return;
            _selectedCatalogNode = value;
            OnPropertyChanged();
            ExportCatalogCommand.RaiseCanExecuteChanged();
            PendingCatalogPreview = SelectCatalogNodeAsync(value);
        }
    }

    /// <summary>The preview load in flight; awaited by the unattended UI self test.</summary>
    public Task? PendingCatalogPreview { get; private set; }

    /// <summary>The catalog build in flight; awaited by the unattended UI self test.</summary>
    public Task? PendingCatalogBuild { get; private set; }

    public RelayCommand ExportCatalogCommand { get; private set; } = null!;

    /// <summary>Parameter is a <see cref="CatalogCategory"/> name; applied to the selected node.</summary>
    public RelayCommand CatalogCategoryCommand { get; private set; } = null!;

    public RelayCommand CatalogAssignChampionCommand { get; private set; } = null!;

    public RelayCommand CatalogHideCommand { get; private set; } = null!;

    public RelayCommand CatalogResetCommand { get; private set; } = null!;

    public RelayCommand RefreshCatalogCommand { get; private set; } = null!;

    /// <summary>Hooks the catalog commands up; called once from the constructor.</summary>
    private void InitializeCatalog()
    {
        ExportCatalogCommand = new RelayCommand(async () => await ExportCatalogAsync(), () => _selectedCatalogNode is not null);
        CatalogCategoryCommand = new RelayCommand(parameter => ApplyCatalogCategory(parameter switch
        {
            CatalogCategory category => category.ToString(),
            CatalogCategoryInfo info => info.Category.ToString(),
            string text => text,
            _ => string.Empty,
        }));
        CatalogAssignChampionCommand = new RelayCommand(() => AssignCatalogChampion(), () => _selectedCatalogNode is { Entry: null });
        CatalogHideCommand = new RelayCommand(() => HideCatalogNode(), () => _selectedCatalogNode is { Entry: null });
        CatalogResetCommand = new RelayCommand(ResetCatalogNode(), () => _catalog is not null);
        RefreshCatalogCommand = new RelayCommand(async () => await BuildCatalogAsync(force: true), () => !_catalogBusy);

        var overrides = CatalogOverrides.Load();
        if (!overrides.IsEmpty) CatalogStatus = $"分类覆盖：{overrides.Rules.Count} 条规则 / {overrides.Entries.Count} 个条目";
    }

    /// <summary>Builds the index on first use (or when an override made it stale).</summary>
    public async Task BuildCatalogAsync(bool force = false)
    {
        if (_catalog is not null && !force) return;
        if (Wads.Count == 0)
        {
            CatalogStatus = "分类索引：先扫描游戏目录";
            return;
        }

        _catalogBusy = true;
        RefreshCatalogCommand.RaiseCanExecuteChanged();
        var summaries = Wads.Select(wad => wad.Summary).ToList();
        var names = _names;
        var clientNames = _clientNames;
        var overrides = CatalogOverrides.Load();

        // Both progress objects must be made here, on the UI thread, or their callbacks arrive on the
        // worker and the log list is written from the wrong thread.
        var progress = new Progress<string>(message => Log("分类：" + message));

        try
        {
            CatalogStatus = "分类索引：读取英雄表…";
            var tables = await Task.Run(() => CatalogTables.LoadFromClient(
                summaries.Where(summary => summary.IsClient).ToList(), clientNames, progress));

            CatalogStatus = "分类索引：分类 98 万个条目…";
            var catalog = await Task.Run(() => AssetCatalog.Build(
                summaries, names, overrides, tables, progress));
            _catalog = catalog;
        }
        catch (Exception ex)
        {
            CatalogStatus = "分类索引失败：" + ex.Message;
            Log("分类索引失败：" + ex.Message);
            return;
        }
        finally
        {
            _catalogBusy = false;
            RefreshCatalogCommand.RaiseCanExecuteChanged();
        }

        BuildCatalogRoots();
        ApplyCatalogSearch();

        var total = _catalog.Count;
        var buckets = _catalog.Groups.Count;
        CatalogStatus = $"分类索引：{total:N0} 个条目 / {buckets:N0} 个分组 · {_catalog.Heroes.Count:N0} 个英雄节点";
        Log(CatalogStatus);
    }

    private void BuildCatalogRoots()
    {
        CatalogRoots.Clear();
        if (_catalog is not { } catalog) return;

        foreach (var info in CatalogCategoryInfo.All)
        {
            var groups = catalog.GroupsOf(info.Category);
            var files = groups.Sum(group => group.Count);
            if (files == 0) continue;

            var heroes = groups.Select(group => group.Hero).Distinct().Count();
            var category = info.Category;
            CatalogRoots.Add(CatalogNodeVm.ForCategory(category, files, heroes,
                node => FillCategoryAsync(node, category), info.Description));
        }
    }

    private Task FillCategoryAsync(CatalogNodeVm node, CatalogCategory category)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;

        var sizes = new Dictionary<int, int>();
        foreach (var group in catalog.GroupsOf(category))
            sizes[group.Hero] = sizes.GetValueOrDefault(group.Hero) + group.Count;

        var rows = sizes
            .Select(pair => (Hero: catalog.Heroes[pair.Key], Size: pair.Value, Index: pair.Key))
            .OrderBy(row => row.Hero.Kind)
            .ThenBy(row => row.Hero.Kind == CatalogHeroKind.Folder ? 1 : 0)
            .ThenBy(row => row.Hero.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        node.Children.Clear();
        foreach (var (hero, size, index) in rows)
        {
            node.Children.Add(CatalogNodeVm.ForHero(
                hero.Label,
                $"{size:N0} 个文件",
                category,
                index,
                hero.Kind == CatalogHeroKind.Guessed,
                hero.Kind == CatalogHeroKind.Folder,
                child => FillHeroAsync(child, category, index)));
        }

        return Task.CompletedTask;
    }

    private Task FillHeroAsync(CatalogNodeVm node, CatalogCategory category, int hero)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;

        // Root files first, then the skins the tables know (by id), then the folders nothing matches
        // (skin05 without a skins.json entry still deserves a row, just further down).
        var groups = catalog.Groups
            .Where(group => group.Category == (int)category && group.Hero == hero)
            .OrderBy(group => group.Skin == 0 ? 0 : catalog.Skins[group.Skin].IsKnown ? 1 : 2)
            .ThenBy(group => catalog.Skins[group.Skin].SkinId)
            .ThenBy(group => catalog.Skins[group.Skin].Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        node.Children.Clear();
        foreach (var group in groups)
        {
            var label = catalog.Skins[group.Skin];
            var name = group.Skin == 0 ? "（根目录）" : label.Label;
            node.Children.Add(CatalogNodeVm.ForSkin(name, $"{group.Count:N0} 个文件", category, hero, group.Skin,
                child => FillSkinAsync(child, group)));
        }

        return Task.CompletedTask;
    }

    private const int CatalogFilesPerNode = 5000;

    private Task FillSkinAsync(CatalogNodeVm node, CatalogGroup group)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;

        var entries = catalog.EntriesOf(group);
        var championFolder = group.Hero >= 0 ? catalog.Heroes[group.Hero].Folder : string.Empty;
        var skinFolder = group.Skin >= 0 ? catalog.Skins[group.Skin].Folder : string.Empty;

        var pairs = entries
            .Select(entry => (Entry: entry, Relative: CatalogPaths.RelativeOf(entry.Path, championFolder, skinFolder)))
            .ToList();

        FillFolder(node, pairs, string.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Fills one folder level of a group: sub folders first, then the files beside them.</summary>
    private static void FillFolder(CatalogNodeVm node, IReadOnlyList<(CatalogEntry Entry, string Relative)> pairs, string prefix)
    {
        node.Children.Clear();

        var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new List<CatalogEntry>();
        foreach (var (entry, relative) in pairs)
        {
            if (prefix.Length > 0 && !relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = relative[prefix.Length..];
            var slash = rest.IndexOf(Path.DirectorySeparatorChar);
            if (slash < 0)
            {
                files.Add(entry);
                continue;
            }

            var folder = rest[..slash];
            folders[folder] = folders.GetValueOrDefault(folder) + 1;
        }

        foreach (var (folder, count) in folders.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var childPrefix = prefix + folder + Path.DirectorySeparatorChar;
            node.Children.Add(CatalogNodeVm.ForFolder(folder, $"{count:N0} 个文件",
                child => Task.Run(() => FillFolder(child, pairs, childPrefix))));
        }

        files.Sort(static (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        foreach (var entry in files.Take(CatalogFilesPerNode)) node.Children.Add(CatalogNodeVm.ForFile(entry));
        if (files.Count > CatalogFilesPerNode)
            node.Children.Add(CatalogNodeVm.Message($"…另有 {files.Count - CatalogFilesPerNode:N0} 个文件，用上面的搜索缩小范围"));
    }

    // ── search ──────────────────────────────────────────────────────────────────────────────

    private void ApplyCatalogSearch()
    {
        CatalogSearchRoots.Clear();
        if (_catalog is not { } catalog)
        {
            RaiseCatalogSearchState();
            return;
        }

        var query = CatalogSearchText.Trim();
        if (query.Length == 0)
        {
            RaiseCatalogSearchState();
            return;
        }

        var result = catalog.Search(query, CatalogFilesPerNode);
        var root = CatalogNodeVm.SearchRoot(query, result.Total, node =>
        {
            node.Children.Clear();
            foreach (var entry in result.Entries) node.Children.Add(CatalogNodeVm.ForFile(entry));
            if (result.Truncated)
                node.Children.Add(CatalogNodeVm.Message($"只列出前 {result.Entries.Count:N0} 个，共 {result.Total:N0} 个命中"));
            return Task.CompletedTask;
        });

        root.IsExpanded = true;
        CatalogSearchRoots.Add(root);
        RaiseCatalogSearchState();
    }

    private void RaiseCatalogSearchState()
    {
        OnPropertyChanged(nameof(CatalogShowingSearch));
        OnPropertyChanged(nameof(CatalogTreeVisible));
    }

    // ── selection and preview ───────────────────────────────────────────────────────────────

    private async Task SelectCatalogNodeAsync(CatalogNodeVm? node)
    {
        if (node?.Entry is not { } entry) return;
        await OpenCatalogEntryAsync(entry);
    }

    /// <summary>Opens one catalog entry in the preview, whatever wad it lives in.</summary>
    private async Task OpenCatalogEntryAsync(CatalogEntry entry)
    {
        var wad = Wads.FirstOrDefault(item => string.Equals(item.FullPath, entry.WadPath, StringComparison.OrdinalIgnoreCase));
        if (wad is null) return;

        _previewWad = wad;
        _previewEntriesOverride = await EntriesOfWadAsync(wad);
        await PreviewEntryAsync(wad, new WadEntryInfo(entry.Hash, entry.Path, entry.Size, 0, "None", false));
    }

    /// <summary>
    /// Jumps to the asset a <c>/lol-game-data/…</c> value of a JSON file points at. The three path
    /// shapes and both locale folders are tried; a miss is reported instead of silently ignored.
    /// </summary>
    public async Task<bool> OpenClientAssetAsync(string reference)
    {
        if (_catalog is null) await BuildCatalogAsync();
        if (_catalog is not { } catalog)
        {
            StatusText = "先扫描游戏目录，才能按路径跳转";
            return false;
        }

        foreach (var candidate in CatalogPaths.ClientCandidates(reference))
        {
            var row = catalog.IndexOf(PathHashing.Compute(candidate));
            if (row < 0) continue;

            var entry = catalog.EntryOf(row);
            await OpenCatalogEntryAsync(entry);
            StatusText = "已跳到 " + entry.Path;
            return true;
        }

        StatusText = "客户端包里没有这个文件：" + reference;
        Log("跳转失败（客户端包里没有）：" + reference);
        return false;
    }

    /// <summary>True while the preview shows JSON; the JSON view replaces the plain text box.</summary>
    public bool ShowJson
    {
        get => _showJson;
        private set => SetProperty(ref _showJson, value);
    }

    private bool _showJson;

    /// <summary>Raw JSON of the previewed entry, formatted and linked by the JSON view.</summary>
    public string? PreviewJson
    {
        get => _previewJson;
        private set => SetProperty(ref _previewJson, value);
    }

    private string? _previewJson;

    /// <summary>Extension of the entry the preview shows, empty when nothing is open.</summary>
    public string PreviewExtension => _previewEntry?.Extension ?? string.Empty;

    /// <summary>Entries of one wad, cached for the few wads the catalog has been browsing.</summary>
    private async Task<IReadOnlyList<WadEntryInfo>> EntriesOfWadAsync(WadItem wad)
    {
        if (_previewEntryCache.TryGetValue(wad.FullPath, out var cached)) return cached;
        if (ReferenceEquals(wad, _selectedWad)) return _currentEntries;

        var names = _names;
        var path = wad.FullPath;
        var entries = await Task.Run(() =>
        {
            using var archive = WadArchive.Open(path);
            return (IReadOnlyList<WadEntryInfo>)archive.ReadEntries(names).ToList();
        });

        if (_previewEntryCache.Count > 8) _previewEntryCache.Clear();
        _previewEntryCache[path] = entries;
        return entries;
    }

    // ── override commands ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Path prefix a rule for the selected node should cover: the folder the node's entries live in,
    /// or the file itself when a single entry is selected.
    /// </summary>
    private string CatalogPrefixOf(CatalogNodeVm node)
    {
        if (node.Entry is { } entry) return entry.Path;

        if (_catalog is not { } catalog) return string.Empty;
        var entries = node switch
        {
            { Category: { } category, Hero: >= 0, Skin: >= 0 } => catalog.EntriesOf(category, node.Hero, node.Skin),
            { Category: { } category, Hero: >= 0 } => catalog.EntriesOf(category, node.Hero),
            { Category: { } category } => catalog.EntriesOf(category),
            _ => Array.Empty<CatalogEntry>(),
        };

        if (entries.Count == 0) return string.Empty;
        var prefix = entries[0].Path;
        foreach (var other in entries)
        {
            var length = 0;
            while (length < prefix.Length && length < other.Path.Length
                   && char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(other.Path[length])) length++;
            prefix = prefix[..length];
        }

        // Cut back to a folder boundary so the rule reads as a folder, not half a file name.
        var slash = prefix.LastIndexOf('/');
        return slash > 0 ? prefix[..slash] : prefix;
    }

    private async void ApplyCatalogCategory(string categoryKey)
    {
        if (_selectedCatalogNode is not { } node) return;
        if (!CatalogCategoryInfo.TryParse(categoryKey, out var category)) return;

        var prefix = CatalogPrefixOf(node);
        if (prefix.Length == 0)
        {
            StatusText = "这个节点没有可用的路径前缀，请选中下面的英雄或皮肤";
            return;
        }

        _catalog?.Overrides.AddRule(new CatalogRule
        {
            PathPrefix = prefix,
            Category = category.ToString(),
            Note = $"界面设置 {DateTime.Now:yyyy-MM-dd HH:mm}",
        });

        Log($"分类覆盖：{prefix} → {CatalogCategoryInfo.Of(category).Display}");
        await BuildCatalogAsync(force: true);
        StatusText = $"已把 {prefix} 归到「{CatalogCategoryInfo.Of(category).Display}」，可随时在右键菜单里还原";
    }

    private async void AssignCatalogChampion()
    {
        if (_selectedCatalogNode is not { } node) return;
        var name = CatalogAssignName.Trim();
        if (name.Length == 0)
        {
            StatusText = "先在「归属到英雄」里填英雄名或目录名";
            return;
        }

        var prefix = CatalogPrefixOf(node);
        if (prefix.Length == 0)
        {
            StatusText = "这个节点没有可用的路径前缀";
            return;
        }

        _catalog?.Overrides.AddRule(new CatalogRule
        {
            PathPrefix = prefix,
            Champion = name,
            Note = $"界面设置 {DateTime.Now:yyyy-MM-dd HH:mm}",
        });

        Log($"分类覆盖：{prefix} 归属到 {name}");
        await BuildCatalogAsync(force: true);
        StatusText = $"{prefix} 已归属到 {name}";
    }

    private async void HideCatalogNode()
    {
        if (_selectedCatalogNode is not { } node) return;
        var prefix = CatalogPrefixOf(node);
        if (prefix.Length == 0) return;

        _catalog?.Overrides.AddRule(new CatalogRule
        {
            PathPrefix = prefix,
            Hidden = true,
            Note = $"界面隐藏 {DateTime.Now:yyyy-MM-dd HH:mm}",
        });

        Log($"分类覆盖：隐藏 {prefix}");
        await BuildCatalogAsync(force: true);
        StatusText = $"已隐藏 {prefix}";
    }

    private Action ResetCatalogNode()
    {
        return () =>
        {
            if (_selectedCatalogNode is not { } node) return;
            var prefix = CatalogPrefixOf(node);
            if (prefix.Length == 0) return;

            var removed = _catalog?.Overrides.RemoveRule(prefix) == true;
            if (node.Entry is { } entry) _catalog?.Overrides.Forget(entry.Hash);
            Log(removed ? $"分类覆盖：还原 {prefix}" : $"分类覆盖：{prefix} 没有手写规则");
            StatusText = removed ? "已还原为自动分类" : "这个节点没有手写规则";
            PendingCatalogBuild = BuildCatalogAsync(force: true);
        };
    }

    // ── export ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Self test entry point: exports the node the catalog currently has selected.</summary>
    public Task ExportSelectedCatalogAsync() => ExportCatalogAsync();

    /// <summary>
    /// Exports what the selected catalog node covers into <c>输出目录\类别\英雄\皮肤\…</c>. The file
    /// list is narrowed to the hashes of the node, so only the chosen part of the wad is written.
    /// </summary>
    private async Task ExportCatalogAsync()
    {
        if (_catalog is not { } catalog || _selectedCatalogNode is not { } node) return;
        if (string.IsNullOrWhiteSpace(OutputRootText))
        {
            StatusText = "请先设置导出目录";
            return;
        }

        var entries = node.Entry is not null
            ? new[] { node.Entry }
            : node switch
            {
                { Category: { } category, Hero: >= 0, Skin: >= 0 } => catalog.EntriesOf(category, node.Hero, node.Skin).ToArray(),
                { Category: { } category, Hero: >= 0 } => catalog.EntriesOf(category, node.Hero).ToArray(),
                { Category: { } category } => catalog.EntriesOf(category).ToArray(),
                _ => Array.Empty<CatalogEntry>(),
            };

        if (entries.Length == 0)
        {
            StatusText = "这个节点下没有可导出的文件";
            return;
        }

        var output = Path.GetFullPath(OutputRootText);
        var destinations = new Dictionary<ulong, string>();
        var wads = new List<string>();
        foreach (var entry in entries)
        {
            var heroFolder = entry.Hero >= 0 ? catalog.Heroes[entry.Hero] : null;
            var skinFolder = entry.Skin > 0 ? catalog.Skins[entry.Skin] : null;
            destinations[entry.Hash] = CatalogPaths.ExportPathOf(entry, heroFolder, skinFolder);

            if (!wads.Contains(entry.WadPath, StringComparer.OrdinalIgnoreCase)) wads.Add(entry.WadPath);
        }

        var filter = new ExtractionFilter
        {
            ExactHashes = new HashSet<ulong>(destinations.Keys),
            IncludeUnnamed = false,
        };

        var options = new ExtractionOptions
        {
            OutputRoot = output,
            GroupByWad = false,
            SkipExisting = OptionSkipExisting,
            Threads = int.TryParse(ThreadCount, out var threads) ? Math.Clamp(threads, 1, 64) : 8,
            ConvertTextures = OptionConvertTextures,
            AudioFormat = SelectedAudioFormat.Value,
            RelativePathResolver = (entry, _) => destinations.TryGetValue(entry.Hash, out var target)
                ? target
                : entry.Path ?? PathHashing.ToText(entry.Hash),
        };

        var label = node.Entry is null ? node.Name : node.Entry.FileName;
        Log($"按分类导出「{label}」：{entries.Length:N0} 个文件 / {wads.Count} 个 wad → {output}");

        await RunBusyAsync("按分类导出…", async () =>
        {
            var progress = new Progress<ExtractionProgress>(p =>
            {
                ProgressValue = p.FilesTotal == 0 ? 0 : p.FilesDone * 100.0 / p.FilesTotal;
                StatusText = $"导出 {p.FilesDone:N0}/{p.FilesTotal:N0} · 成功 {p.Extracted:N0} · 跳过 {p.Skipped:N0}";
            });

            var root = LolInstall.TryLocate(DataRootText) ?? string.Empty;
            var summary = await Task.Run(() =>
                ExtractionService.Extract(root, wads, _names, filter, options, progress, null, _cancellation!.Token));

            ProgressValue = 100;
            StatusText = summary.Describe();
            Log(summary.ManifestPath is null ? summary.Describe() : $"{summary.Describe()}；清单 {Path.GetFileName(summary.ManifestPath)}");
            foreach (var error in summary.Errors.Take(5)) Log("失败：" + error);
        });
    }
}
