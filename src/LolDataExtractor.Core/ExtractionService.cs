using System.Diagnostics;
using System.Text.Json;

namespace LolDataExtractor.Core;

/// <summary>Which tree a wad belongs to.</summary>
public static class WadSources
{
    /// <summary>The game archives below <c>Game\DATA</c>.</summary>
    public const string Game = "game";

    /// <summary>The client plugin archives below <c>LeagueClient\plugins</c>.</summary>
    public const string Client = "client";
}

/// <summary>Summary of a single wad file, shown in the wad list.</summary>
public sealed record WadSummary(
    string FullPath,
    string RelativePath,
    string Name,
    long FileSize,
    int EntryCount,
    int RedirectCount,
    long UncompressedSize,
    IReadOnlyDictionary<string, int> StorageTypes)
{
    /// <summary>Game DATA archive or client plugin archive; see <see cref="WadSources"/>.</summary>
    public string Source { get; init; } = WadSources.Game;

    public bool IsClient => Source == WadSources.Client;

    public double CompressionRatio => UncompressedSize == 0 ? 0 : (double)FileSize / UncompressedSize;

    public string StorageSummary => string.Join(", ", StorageTypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}"));
}

public static class WadCatalog
{
    public static WadSummary Describe(string dataRoot, string wadPath, string source = WadSources.Game, string? relativePath = null)
    {
        var storageTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var redirects = 0;
        long uncompressed = 0;
        var count = 0;

        using (var archive = WadArchive.Open(wadPath))
        {
            foreach (var entry in archive.ReadEntries(names: null))
            {
                count++;
                uncompressed += entry.UncompressedSize;
                storageTypes[entry.Storage] = storageTypes.GetValueOrDefault(entry.Storage) + 1;
                if (entry.IsRedirection) redirects++;
            }
        }

        var info = new FileInfo(wadPath);
        return new WadSummary(
            wadPath,
            relativePath ?? (dataRoot.Length > 0 ? LolInstall.GetRelativeName(dataRoot, wadPath) : Path.GetFileName(wadPath)),
            Path.GetFileName(wadPath),
            info.Length,
            count,
            redirects,
            uncompressed,
            storageTypes)
        {
            Source = source,
        };
    }

    public static IReadOnlyList<WadSummary> Scan(
        string dataRoot,
        IProgress<WadSummary>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WadSummary>();
        foreach (var wadPath in LolInstall.EnumerateWadFiles(dataRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var summary = Describe(dataRoot, wadPath);
                results.Add(summary);
                progress?.Report(summary);
            }
            catch (Exception ex)
            {
                progress?.Report(new WadSummary(wadPath, LolInstall.GetRelativeName(dataRoot, wadPath), Path.GetFileName(wadPath), new FileInfo(wadPath).Length, 0, 0, 0, new Dictionary<string, int> { [ex.GetType().Name] = 1 }));
            }
        }
        return results;
    }

    /// <summary>
    /// Scans the client plugin archives (<c>LeagueClient\plugins\*.wad</c>). They use their own name
    /// table, so their summaries are tagged with <see cref="WadSources.Client"/> and their relative
    /// path starts with <see cref="LolClient.RelativePrefix"/>.
    /// </summary>
    public static IReadOnlyList<WadSummary> ScanClient(
        string pluginsRoot,
        IProgress<WadSummary>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WadSummary>();
        foreach (var wadPath in LolClient.EnumerateWadFiles(pluginsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = LolClient.RelativePath(pluginsRoot, wadPath);
            try
            {
                var summary = Describe(pluginsRoot, wadPath, WadSources.Client, relative);
                results.Add(summary);
                progress?.Report(summary);
            }
            catch (Exception ex)
            {
                progress?.Report(new WadSummary(wadPath, relative, Path.GetFileName(wadPath), new FileInfo(wadPath).Length, 0, 0, 0, new Dictionary<string, int> { [ex.GetType().Name] = 1 }) { Source = WadSources.Client });
            }
        }
        return results;
    }
}

/// <summary>Selection rules for extraction.</summary>
public sealed class ExtractionFilter
{
    public string? PathPattern { get; set; }

    /// <summary>Only keep entries below this folder, e.g. <c>assets/characters/ahri</c>.</summary>
    public string? PathPrefix { get; set; }

    /// <summary>
    /// Only keep these exact entry paths, e.g. the one node picked in the browser. Matching is
    /// case-insensitive and accepts either slash, so a path copied from the tree works as is.
    /// </summary>
    public HashSet<string>? ExactPaths { get; set; }

    /// <summary>
    /// Only keep these entries, addressed by hash. This is what the tick boxes of the browser
    /// collect, and unlike <see cref="ExactPaths"/> it also covers entries without a known path.
    /// </summary>
    public HashSet<ulong>? ExactHashes { get; set; }

    public HashSet<string>? Categories { get; set; }

    public HashSet<string>? Extensions { get; set; }

    public long? MinSize { get; set; }

    public long? MaxSize { get; set; }

    public bool IncludeUnnamed { get; set; } = true;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(PathPattern) &&
        string.IsNullOrWhiteSpace(PathPrefix) &&
        (ExactPaths is null || ExactPaths.Count == 0) &&
        (ExactHashes is null || ExactHashes.Count == 0) &&
        (Categories is null || Categories.Count == 0) &&
        (Extensions is null || Extensions.Count == 0) &&
        MinSize is null && MaxSize is null &&
        IncludeUnnamed;

    private global::System.Text.RegularExpressions.Regex? _pattern;

    private string? _prefix;
    private bool _prefixReady;

    private HashSet<string>? _exact;
    private bool _exactReady;

    /// <summary>Folder prefix of <see cref="PathPrefix"/>, normalised once per filter.</summary>
    private string? Prefix
    {
        get
        {
            if (_prefixReady) return _prefix;
            _prefixReady = true;
            _prefix = NormalizePrefix(PathPrefix);
            return _prefix;
        }
    }

    private global::System.Text.RegularExpressions.Regex? Pattern
    {
        get
        {
            if (_pattern is not null) return _pattern;
            if (string.IsNullOrWhiteSpace(PathPattern)) return null;
            _pattern = GlobToRegex(PathPattern.Trim());
            return _pattern;
        }
    }

    /// <summary>Case-insensitive set of <see cref="ExactPaths"/>, normalised once per filter.</summary>
    private HashSet<string>? Exact
    {
        get
        {
            if (_exactReady) return _exact;
            _exactReady = true;
            if (ExactPaths is not { Count: > 0 } paths) return _exact = null;
            _exact = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeExactPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _exact;
        }
    }

    /// <summary>Forward slashes, no surrounding slash: the shape both tree paths and filters use.</summary>
    private static string NormalizeExactPath(string path) => path.Replace('\\', '/').Trim().Trim('/');

    public bool Matches(WadEntryInfo entry)
    {
        if (!IncludeUnnamed && !entry.IsNamed) return false;
        if (MinSize is { } min && entry.UncompressedSize < min) return false;
        if (MaxSize is { } max && entry.UncompressedSize > max) return false;

        if (ExactHashes is { Count: > 0 } hashes && !hashes.Contains(entry.Hash)) return false;

        if (Exact is { Count: > 0 } exact)
        {
            var exactPath = entry.Path is null ? null : NormalizeExactPath(entry.Path);
            if (exactPath is null || !exact.Contains(exactPath)) return false;
        }

        if (Extensions is { Count: > 0 } extensions)
        {
            var ext = entry.Extension;
            if (ext.Length == 0 || !extensions.Contains(ext)) return false;
        }

        if (Categories is { Count: > 0 } categories && !categories.Contains(entry.Category))
            return false;

        if (Prefix is { Length: > 0 } prefix)
        {
            var folder = entry.Path?.Replace('\\', '/');
            if (folder is null) return false;
            if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }

        var pattern = Pattern;
        if (pattern is null) return true;

        var target = entry.Path ?? PathHashing.ToText(entry.Hash);
        return pattern.IsMatch(target);
    }

    /// <summary>Turns a folder into a "starts with" prefix: forward slashes and a trailing one.</summary>
    public static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;

        var trimmed = prefix.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0) return null;
        return trimmed + "/";
    }

    public static global::System.Text.RegularExpressions.Regex GlobToRegex(string glob)
    {
        var builder = new global::System.Text.StringBuilder("^");
        foreach (var character in glob)
        {
            builder.Append(character switch
            {
                '*' => ".*",
                '?' => ".",
                _ => global::System.Text.RegularExpressions.Regex.Escape(character.ToString()),
            });
        }
        builder.Append('$');
        return new global::System.Text.RegularExpressions.Regex(
            builder.ToString(),
            global::System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            global::System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}

public sealed class ExtractionOptions
{
    public required string OutputRoot { get; init; }

    /// <summary>Put every entry of a wad below a folder named after that wad.</summary>
    public bool GroupByWad { get; init; } = true;

    /// <summary>Write files flat into the wad folder instead of recreating the asset tree.</summary>
    public bool FlatLayout { get; init; }

    public bool SkipExisting { get; init; } = true;

    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);

    public bool WriteManifest { get; init; } = true;

    /// <summary>Write <c>.tex</c>/<c>.dds</c> as png instead of the raw container.</summary>
    public bool ConvertTextures { get; init; } = true;

    /// <summary>How to write Wwise audio: keep the container, or decode it to wav/mp3.</summary>
    public AudioExportFormat AudioFormat { get; init; } = AudioExportFormat.Mp3;

    /// <summary>
    /// Export only the second pass media: an entry that cannot be turned into png / mp3 is left out
    /// instead of being written in its raw form.
    /// </summary>
    public bool MediaOnly { get; init; }

    /// <summary>True when the export has to decode something, i.e. needs the helper tools.</summary>
    public bool ConvertsMedia => ConvertTextures || AudioFormat != AudioExportFormat.Keep;

    /// <summary>
    /// Replaces the path written below the output folder. The catalog uses it to lay files out as
    /// category / hero / skin; <paramref name="wadKey"/> is the wad name without its suffix.
    /// </summary>
    public Func<WadEntryInfo, string, string>? RelativePathResolver { get; init; }
}

public sealed record ExtractionProgress(
    string Wad,
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    int Extracted,
    int Skipped,
    int Failed,
    string? CurrentFile);

public sealed record ExtractionSummary(
    int Extracted,
    int Skipped,
    int Failed,
    long BytesWritten,
    TimeSpan Elapsed,
    string? ManifestPath,
    IReadOnlyList<string> Errors)
{
    public string Describe() =>
        $"导出 {Extracted} 个文件，跳过 {Skipped}，失败 {Failed}，共 {Format(BytesWritten)}，耗时 {Elapsed.TotalSeconds:F1}s";

    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>Extracts wad entries to disk, honouring name resolution and filters.</summary>
public static class ExtractionService
{
    private const int MaxErrors = 50;

    public static ExtractionSummary Extract(
        string dataRoot,
        IReadOnlyList<string> wadPaths,
        NameTable names,
        ExtractionFilter filter,
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(options.OutputRoot);

        var extracted = 0;
        var skipped = 0;
        var failed = 0;
        long bytesWritten = 0;
        var errors = new List<string>();
        string? manifestPath = null;

        StreamWriter? manifest = null;
        if (options.WriteManifest)
        {
            manifestPath = Path.Combine(options.OutputRoot, $"extract-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            manifest = new StreamWriter(manifestPath, append: false, new global::System.Text.UTF8Encoding(false));
        }

        try
        {
            // Resolve every wad's work list first so the progress bar can be meaningful.
            var plans = new List<(string WadPath, string WadKey, List<WadEntryInfo> Entries)>();
            foreach (var wadPath in wadPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = WadKeyOf(wadPath);
                List<WadEntryInfo> entries;
                using (var archive = WadArchive.Open(wadPath))
                {
                    entries = new List<WadEntryInfo>();
                    foreach (var entry in archive.ReadEntries(names))
                    {
                        if (entry.IsRedirection || entry.UncompressedSize <= 0) continue;
                        if (!filter.Matches(entry)) continue;
                        entries.Add(entry);
                    }
                }
                if (entries.Count == 0) continue;
                plans.Add((wadPath, key, entries));
                log?.Invoke($"{Path.GetFileName(wadPath)}: 选中 {entries.Count} 个条目");
            }

            var totalFiles = plans.Sum(p => p.Entries.Count);
            var doneFiles = 0;
            var doneBytes = 0L;

            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wadFolder = options.GroupByWad ? plan.WadKey : string.Empty;

                using var threadLocal = new ThreadLocal<WadArchive>(() => WadArchive.Open(plan.WadPath), trackAllValues: true);
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.Threads),
                    CancellationToken = cancellationToken,
                };

                try
                {
                    Parallel.ForEach(plan.Entries, parallelOptions, entry =>
                    {
                        var archive = threadLocal.Value!;
                        var relative = options.RelativePathResolver is { } layout
                            ? layout(entry, plan.WadKey)
                            : ResolveRelativePath(plan.WadKey, entry, archive, options.FlatLayout);
                        var destination = SafePath.Fit(options.OutputRoot, wadFolder, relative);

                        try
                        {
                            var target = MediaExporter.PredictTarget(destination, entry.Extension, options);
                            if (options.SkipExisting && (File.Exists(target) || Directory.Exists(target)))
                            {
                                Interlocked.Increment(ref skipped);
                                Interlocked.Add(ref doneBytes, entry.UncompressedSize);
                            }
                            else
                            {
                                var outputs = MediaExporter.Export(archive, entry, destination, options, message => log?.Invoke(message));
                                if (outputs.Count == 0)
                                {
                                    // Media-only export: nothing playable came out, so nothing is written.
                                    Interlocked.Increment(ref skipped);
                                    Interlocked.Add(ref doneBytes, entry.UncompressedSize);
                                    return;
                                }

                                Interlocked.Increment(ref extracted);
                                var written = SizeOf(outputs);
                                Interlocked.Add(ref bytesWritten, written);
                                Interlocked.Add(ref doneBytes, entry.UncompressedSize);
                                if (manifest is not null)
                                {
                                    var line = JsonSerializer.Serialize(new
                                    {
                                        wad = plan.WadKey,
                                        hash = PathHashing.ToText(entry.Hash),
                                        path = entry.Path,
                                        size = entry.UncompressedSize,
                                        file = Path.GetRelativePath(options.OutputRoot, outputs[0]).Replace('\\', '/'),
                                        outputs = outputs.Select(output => Path.GetRelativePath(options.OutputRoot, output).Replace('\\', '/')).ToArray(),
                                        bytes = written,
                                    });
                                    lock (manifest) manifest.WriteLine(line);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            lock (errors)
                            {
                                if (errors.Count < MaxErrors) errors.Add($"{entry.Path ?? PathHashing.ToText(entry.Hash)}: {ex.Message}");
                            }
                        }

                        var done = Interlocked.Increment(ref doneFiles);
                        var bytes = Interlocked.Read(ref doneBytes);
                        if (done % 16 == 0 || done == totalFiles)
                        {
                            progress?.Report(new ExtractionProgress(
                                plan.WadKey,
                                done,
                                totalFiles,
                                bytes,
                                Volatile.Read(ref extracted),
                                Volatile.Read(ref skipped),
                                Volatile.Read(ref failed),
                                entry.Path ?? PathHashing.ToText(entry.Hash)));
                        }
                    });
                }
                finally
                {
                    foreach (var archive in threadLocal.Values) archive.Dispose();
                }

                log?.Invoke($"{plan.WadKey}: 完成（累计导出 {Volatile.Read(ref extracted)}）");
            }
        }
        finally
        {
            manifest?.Dispose();
        }

        stopwatch.Stop();
        return new ExtractionSummary(extracted, skipped, failed, bytesWritten, stopwatch.Elapsed, manifestPath, errors);
    }

    /// <summary>Bytes on disk of what an entry produced; a stream folder counts all its files.</summary>
    private static long SizeOf(IReadOnlyList<string> outputs)
    {
        long total = 0;
        foreach (var output in outputs)
        {
            if (Directory.Exists(output))
            {
                foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch (IOException) { }
                }
            }
            else if (File.Exists(output))
            {
                try { total += new FileInfo(output).Length; } catch (IOException) { }
            }
        }
        return total;
    }

    private static string ResolveRelativePath(string wadKey, WadEntryInfo entry, WadArchive archive, bool flat)
    {
        string relative;
        if (entry.IsNamed)
        {
            relative = NormalizeAssetPath(entry.Path!);
        }
        else
        {
            var extension = SniffExtension(entry, archive);
            var hashText = PathHashing.ToText(entry.Hash);
            relative = Path.Combine("_unnamed", hashText[..2], $"{hashText}.{extension}");
        }

        if (!flat) return relative;
        return entry.IsNamed
            ? $"{wadKey}_{Path.GetFileName(relative)}"
            : relative;
    }

    /// <summary>Reads the entry header to give unnamed assets a usable extension.</summary>
    private static string SniffExtension(WadEntryInfo entry, WadArchive archive)
    {
        if (entry.Extension.Length > 0) return entry.Extension;
        try
        {
            var head = archive.ReadHead(entry.Hash, 64);
            return FileTypeSniffer.DetectExtension(head) ?? "bin";
        }
        catch (Exception)
        {
            return "bin";
        }
    }

    /// <summary>"Ahri.wad.client" becomes "Ahri", "Akali.zh_CN.wad.client" becomes "Akali.zh_CN".</summary>
    public static string WadKeyOf(string wadPath)
    {
        var name = Path.GetFileName(wadPath);
        const string suffix = ".wad.client";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// The relative path used when a single entry is exported on its own. Audio (wem/bnk/wpk)
    /// keeps only the container's own name, so a voice package lands in
    /// <c>输出目录\wad 名\包名.wpk\</c> instead of repeating the whole wad folder tree; everything
    /// else keeps the structure it has inside the wad.
    /// </summary>
    public static string SingleExportRelativePath(WadEntryInfo entry)
    {
        var relative = entry.IsNamed
            ? NormalizeAssetPath(entry.Path!)
            : Path.Combine("_unnamed", PathHashing.ToText(entry.Hash) + "." + (entry.Extension.Length == 0 ? "bin" : entry.Extension));

        return MediaExporter.IsConvertibleAudio(entry.Extension) ? Path.GetFileName(relative) : relative;
    }

    public static string NormalizeAssetPath(string path)
    {
        var cleaned = path.Replace('\\', '/').TrimStart('/');
        var segments = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "." and not "..")
            .Select(segment => string.Join('_', segment.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)));
        return Path.Combine(segments.ToArray());
    }
}
