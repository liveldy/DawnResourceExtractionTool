using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.ViewModels;

/// <summary>
/// The classified browser: section → group → entity → folder → file, built from every wad at once.
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

    /// <summary>Name typed into the "归属到组" box; a champion alias, a series or a free label.</summary>
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

    /// <summary>Parameter is a <see cref="CatalogSection"/> name; applied to the selected node.</summary>
    public RelayCommand CatalogSectionCommand { get; private set; } = null!;

    public RelayCommand CatalogAssignGroupCommand { get; private set; } = null!;

    public RelayCommand CatalogHideCommand { get; private set; } = null!;

    public RelayCommand CatalogResetCommand { get; private set; } = null!;

    public RelayCommand RefreshCatalogCommand { get; private set; } = null!;

    /// <summary>Hooks the catalog commands up; called once from the constructor.</summary>
    private void InitializeCatalog()
    {
        ExportCatalogCommand = new RelayCommand(async () => await ExportCatalogAsync(), () => _selectedCatalogNode is not null);
        CatalogSectionCommand = new RelayCommand(parameter => ApplyCatalogSection(parameter switch
        {
            CatalogSection section => section.ToString(),
            CatalogSectionInfo info => info.Section.ToString(),
            string text => text,
            _ => string.Empty,
        }));
        CatalogAssignGroupCommand = new RelayCommand(() => AssignCatalogGroup(), () => _selectedCatalogNode is { Entry: null });
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
            CatalogStatus = "分类索引：读取客户端数据表…";
            var tables = await Task.Run(() => CatalogTables.LoadFromClient(
                summaries.Where(summary => summary.IsClient).ToList(), clientNames, progress));

            CatalogStatus = "分类索引：按数据表归堆…";
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

        CatalogStatus = $"分类索引：{_catalog.Count:N0} 个条目 · {_catalog.Groups.Count(group => group.Count > 0):N0} 个组 · " +
                        $"{_catalog.Entities.Count(entity => entity.Count > 0):N0} 个条目节点";
        Log(CatalogStatus);
    }

    private void BuildCatalogRoots()
    {
        CatalogRoots.Clear();
        if (_catalog is not { } catalog) return;

        foreach (var info in CatalogSectionInfo.All)
        {
            var groups = catalog.GroupsOf(info.Section);
            var files = groups.Sum(group => group.Count);
            if (files == 0) continue;

            var section = info.Section;
            CatalogRoots.Add(CatalogNodeVm.ForSection(info, files, groups.Count,
                node => FillSectionAsync(node, section), info.Description));
        }
    }

    private Task FillSectionAsync(CatalogNodeVm node, CatalogSection section)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;

        var rows = new List<(int Index, CatalogGroup Group)>();
        for (var i = 0; i < catalog.Groups.Count; i++)
        {
            var group = catalog.Groups[i];
            if (group.Section == section && group.Count > 0) rows.Add((i, group));
        }

        node.Children.Clear();
        foreach (var (index, group) in rows
                     .OrderBy(row => row.Group.Rank)
                     .ThenBy(row => row.Group.Label, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(CatalogNodeVm.ForGroup(section, index, group.Label, $"{group.Count:N0} 个文件",
                null, group.Detail, child => FillGroupAsync(child, index)));
        }

        return Task.CompletedTask;
    }

    private Task FillGroupAsync(CatalogNodeVm node, int group)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;

        var rows = new List<(int Index, CatalogEntity Entity)>();
        for (var i = 0; i < catalog.Entities.Count; i++)
        {
            var entity = catalog.Entities[i];
            if (entity.Group == group && entity.Count > 0) rows.Add((i, entity));
        }

        node.Children.Clear();
        foreach (var (index, entity) in rows
                     .OrderBy(row => row.Entity.Rank)
                     .ThenBy(row => row.Entity.Label, StringComparer.OrdinalIgnoreCase))
        {
            node.Children.Add(CatalogNodeVm.ForEntity(node.Section ?? CatalogSection.Champion, group, index,
                entity.Label, $"{entity.Count:N0} 个文件", entity.Detail,
                child => FillEntityAsync(child, group, index)));
        }

        return Task.CompletedTask;
    }

    private const int CatalogFilesPerNode = 5000;

    private Task FillEntityAsync(CatalogNodeVm node, int group, int entity)
    {
        if (_catalog is not { } catalog) return Task.CompletedTask;
        if (entity < 0 || entity >= catalog.Entities.Count) return Task.CompletedTask;

        var anchor = catalog.Entities[entity].Anchor;
        var pairs = catalog.EntriesOf(group, entity)
            .Select(entry => (Entry: entry, Relative: CatalogPaths.RelativeOf(entry.Path, anchor)))
            .ToList();

        FillFolder(node, pairs, string.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Fills one folder level of an entity: sub folders first, then the files beside them.</summary>
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
            foreach (var entry in result.Entries) node.Children.Add(CatalogNodeVm.ForFile(entry, CatalogLocationOf(entry)));
            if (result.Truncated)
                node.Children.Add(CatalogNodeVm.Message($"只列出前 {result.Entries.Count:N0} 个，共 {result.Total:N0} 个命中"));
            return Task.CompletedTask;
        });

        root.IsExpanded = true;
        CatalogSearchRoots.Add(root);
        RaiseCatalogSearchState();
    }

    /// <summary>"英雄 · Ahri · Base" for one search hit, so a result says where it lives.</summary>
    private string CatalogLocationOf(CatalogEntry entry)
    {
        if (_catalog is not { } catalog) return CatalogSectionInfo.Of(entry.Section).Display;
        var group = entry.Group >= 0 && entry.Group < catalog.Groups.Count ? catalog.Groups[entry.Group].Label : string.Empty;
        var entity = entry.Entity >= 0 && entry.Entity < catalog.Entities.Count ? catalog.Entities[entry.Entity].Label : string.Empty;
        var parts = new[] { CatalogSectionInfo.Of(entry.Section).Display, group, entity }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" · ", parts);
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

    private IReadOnlyList<CatalogEntry> EntriesOfNode(CatalogNodeVm node)
    {
        if (_catalog is not { } catalog) return Array.Empty<CatalogEntry>();
        if (node.Entry is { } entry) return new[] { entry };
        if (node.Group >= 0 && node.Entity >= 0) return catalog.EntriesOf(node.Group, node.Entity);
        if (node.Group >= 0) return catalog.EntriesOf(node.Group);
        if (node.Section is { } section) return catalog.EntriesOf(section);
        return Array.Empty<CatalogEntry>();
    }

    /// <summary>
    /// Path prefix a rule for the selected node should cover: the folder the node's entries live in,
    /// or the file itself when a single entry is selected.
    /// </summary>
    private string CatalogPrefixOf(CatalogNodeVm node)
    {
        if (node.Entry is { } entry) return entry.Path;

        var entries = EntriesOfNode(node);
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

    private async void ApplyCatalogSection(string sectionKey)
    {
        if (_selectedCatalogNode is not { } node) return;
        if (!CatalogSectionInfo.TryParse(sectionKey, out var section)) return;

        var prefix = CatalogPrefixOf(node);
        if (prefix.Length == 0)
        {
            StatusText = "这个节点没有可用的路径前缀，请选中下面的组或条目";
            return;
        }

        _catalog?.Overrides.AddRule(new CatalogRule
        {
            PathPrefix = prefix,
            Section = section.ToString(),
            Note = $"界面设置 {DateTime.Now:yyyy-MM-dd HH:mm}",
        });

        Log($"分类覆盖：{prefix} → {CatalogSectionInfo.Of(section).Display}");
        await BuildCatalogAsync(force: true);
        StatusText = $"已把 {prefix} 归到「{CatalogSectionInfo.Of(section).Display}」，可随时在右键菜单里还原";
    }

    private async void AssignCatalogGroup()
    {
        if (_selectedCatalogNode is not { } node) return;
        var name = CatalogAssignName.Trim();
        if (name.Length == 0)
        {
            StatusText = "先在「归属到组」里填英雄别名或组名";
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
            Group = name,
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
    /// Exports what the selected catalog node covers into <c>输出目录\类别\组\条目\…</c>. The file
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

        var entries = EntriesOfNode(node);
        if (entries.Count == 0)
        {
            StatusText = "这个节点下没有可导出的文件";
            return;
        }

        var output = Path.GetFullPath(OutputRootText);
        var destinations = new Dictionary<ulong, string>();
        var wads = new List<string>();

        // The same node can cover several entities (a whole section or group), and the export writes one
        // aggregated info.json at its root, so group the entries by the entity they land in.
        var byEntity = new Dictionary<int, List<CatalogInfoEntry>>();
        var sizes = new Dictionary<int, long>();
        foreach (var entry in entries)
        {
            var section = CatalogSectionInfo.Of(entry.Section);
            var entity = catalog.Entities[entry.Entity];
            destinations[entry.Hash] = CatalogPaths.ExportPathOf(entry, section, catalog.Groups[entry.Group], entity);

            var relative = CatalogPaths.RelativeOf(entry.Path, entity.Anchor);
            if (relative.Length == 0) relative = CatalogPaths.SafeFolderOf(entry.FileName);
            if (!byEntity.TryGetValue(entry.Entity, out var files)) byEntity[entry.Entity] = files = new();
            files.Add(new CatalogInfoEntry(relative, entry.Path, PathHashing.ToText(entry.Hash),
                entry.Size, Path.GetFileName(entry.WadPath), entry.Extension));
            sizes[entry.Entity] = sizes.GetValueOrDefault(entry.Entity) + entry.Size;

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
        Log($"按分类导出「{label}」：{entries.Count:N0} 个文件 / {wads.Count} 个 wad → {output}");

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

            var info = await Task.Run(() => WriteCatalogInfo(output, catalog, byEntity, sizes));
            Log($"信息 JSON：{Path.Combine(output, CatalogInfo.FileName)}" +
                $"（{info.EntityCount:N0} 个条目 / {info.FileCount:N0} 个文件）");

            // 相关数据表：把客户端描述这段内容的原始 json 转写进导出根目录，而不是只给文件清单。
            var tables = await Task.Run(() => CatalogJsonExport.Write(output, catalog.Tables, entries, destinations));
            foreach (var table in tables)
                Log($"相关 JSON：{table.Table}（保留 {table.Records:N0}/{table.Total:N0} 条）→ {table.Path}");
        });
    }

    /// <summary>
    /// Writes one aggregated <c>info.json</c> into the export root: every entity the export wrote, the
    /// node it belongs to, plus the original path, hash, size and wad of each file, so an exported tree
    /// still explains itself after it has been moved away from the tool.
    /// </summary>
    private static CatalogInfoDocument WriteCatalogInfo(string output, AssetCatalog catalog,
        IReadOnlyDictionary<int, List<CatalogInfoEntry>> byEntity, IReadOnlyDictionary<int, long> sizes)
    {
        var entities = new List<CatalogInfoEntity>();
        foreach (var (entityIndex, files) in byEntity)
        {
            var entity = catalog.Entities[entityIndex];
            var group = catalog.Groups[entity.Group];
            var section = CatalogSectionInfo.Of(group.Section);

            entities.Add(new CatalogInfoEntity(
                section.Display, group.Label, entity.Label, entity.Anchor,
                CatalogPaths.EntityFolderOf(section, group, entity),
                files.Count, sizes.GetValueOrDefault(entityIndex), files));
        }

        return CatalogInfo.Write(output, $"LolDataExtractor {AppInfo.Version}", entities);
    }
}
