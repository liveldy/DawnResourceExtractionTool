using System.Diagnostics;
using System.Text;
using DawnResourceExtractionTool.Core;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        SetLanguage(args);
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Strings.T("Cli.Error", ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// <c>--lang zh|en</c> picks the output language and is read before anything else is printed. The
    /// command line keeps Chinese unless asked otherwise; it does not follow the system language.
    /// </summary>
    private static void SetLanguage(string[] args)
    {
        var value = CommandLine.Parse(args).Get("lang");
        Strings.Language = value is null ? AppLanguage.Chinese : AppLanguages.Parse(value);
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var parsed = CommandLine.Parse(args.Skip(1).ToArray());
        var settings = AppSettings.Load();

        return command switch
        {
            "info" => Info(settings, parsed),
            "scan" => Scan(settings, parsed),
            "list" => List(settings, parsed),
            "extract" => Extract(settings, parsed),
            "media" => Extract(settings, parsed, mediaOnly: true),
            "catalog" => Catalog(settings, parsed),
            "hashes" => Hashes(settings, parsed),
            _ => Unknown(command),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine(Strings.T("Cli.UnknownCommand", command));
        PrintHelp();
        return 2;
    }

    private static void PrintHelp() => Console.WriteLine(Strings.T("Cli.Help"));

    /// <summary>
    /// 游戏 DATA 目录（规范化后写回配置）。<paramref name="clientRoot"/> 是同一个路径下的客户端
    /// 包目录：指到安装根目录就有，指到 <c>Game\DATA</c> 就没有——扫什么是路径决定的。
    /// </summary>
    private static string? ResolveRoot(AppSettings settings, CommandLine parsed, out string? clientRoot)
    {
        var root = parsed.Get("root") ?? settings.DataRoot;
        clientRoot = LolClient.TryLocatePlugins(root);
        var resolved = LolInstall.TryLocate(root);
        if (resolved is null)
        {
            Console.Error.WriteLine(Strings.T("Cli.Root.Unrecognized", root ?? Strings.T("Cli.Info.NotSet")));
            Console.Error.WriteLine(Strings.T("Cli.Root.Hint"));
        }
        else if (settings.DataRoot != resolved)
        {
            settings.DataRoot = resolved;
            settings.Save();
        }
        return resolved;
    }

    private static NameTable LoadNames(AppSettings settings)
    {
        var client = LoadClientNames(settings);

        foreach (var path in new[] { settings.NamesIndexPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                return NameTable.LoadBinary(path).Union(client);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Strings.T("Cli.Names.ReadFailed", ex.Message));
            }
        }

        var textFiles = HashTableFetcher.FindTextTables(settings.HashTableDirectory);
        if (textFiles.Count > 0)
        {
            Console.WriteLine(Strings.T("Cli.Names.ParsingTables", textFiles.Count));
            var table = NameTable.LoadTextFiles(textFiles);
            Console.WriteLine(Strings.T("Cli.Names.Loaded", table.Count));
            return table.Union(client);
        }

        if (!client.IsEmpty) return client;

        Console.WriteLine(Strings.T("Cli.Names.Missing"));
        return NameTable.Empty;
    }

    /// <summary>The client (LCU) name table, empty when it was never downloaded.</summary>
    private static NameTable LoadClientNames(AppSettings settings)
    {
        try
        {
            return NameTable.LoadBinaryOrEmpty(settings.ClientNamesPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Strings.T("Cli.Names.ClientReadFailed", ex.Message));
            return NameTable.Empty;
        }
    }

    private static int Info(AppSettings settings, CommandLine parsed)
    {
        var hint = parsed.Get("root") ?? settings.DataRoot;
        var dataRoot = LolInstall.TryLocate(hint);
        var clientRoot = LolClient.TryLocatePlugins(hint);
        Console.WriteLine(Strings.T("Cli.Info.Title", AppInfo.Version, AppInfo.ReleaseDate));
        Console.WriteLine(Strings.T("Cli.Info.GameData", dataRoot ?? Strings.T("Cli.Info.NotLocated")));
        Console.WriteLine(Strings.T("Cli.Info.OutputRoot", settings.OutputRoot));
        Console.WriteLine(Strings.T("Cli.Info.DataDirectory", settings.DataDirectory));
        Console.WriteLine(Strings.T("Cli.Info.HashDirectory", settings.HashTableDirectory));
        Console.WriteLine(Strings.T("Cli.Info.NameIndex", settings.NamesIndexPath,
            Strings.T(File.Exists(settings.NamesIndexPath) ? "Cli.Info.Built" : "Cli.Info.NotBuilt")));
        Console.WriteLine(Strings.T("Cli.Info.ClientPlugins", clientRoot ?? Strings.T("Cli.Info.ClientNotIncluded")));
        Console.WriteLine(Strings.T("Cli.Info.ClientNames", settings.ClientNamesPath,
            Strings.T(File.Exists(settings.ClientNamesPath) ? "Cli.Info.Built" : "Cli.Info.NotBuilt")));

        if (dataRoot is not null)
        {
            var wads = LolInstall.EnumerateWadFiles(dataRoot);
            Console.WriteLine(Strings.T("Cli.Info.WadCount", wads.Count));
            var totalSize = wads.Sum(w => new FileInfo(w).Length);
            Console.WriteLine(Strings.T("Cli.Info.WadSize", ExtractionSummary.Format(totalSize)));
        }

        if (clientRoot is not null)
        {
            var clientWads = LolClient.EnumerateWadFiles(clientRoot);
            Console.WriteLine(Strings.T("Cli.Info.ClientWadCount", clientWads.Count));
            Console.WriteLine(Strings.T("Cli.Info.ClientWadSize", ExtractionSummary.Format(clientWads.Sum(w => new FileInfo(w).Length))));
        }

        if (File.Exists(settings.NamesIndexPath))
        {
            var names = NameTable.LoadBinary(settings.NamesIndexPath);
            Console.WriteLine(Strings.T("Cli.Info.NameCount", names.Count));
        }

        return 0;
    }

    private static int Scan(AppSettings settings, CommandLine parsed)
    {
        var dataRoot = ResolveRoot(settings, parsed, out var clientRoot);
        if (dataRoot is null) return 1;

        Console.WriteLine(Strings.T("Cli.Scan.Scanning", dataRoot));
        var timer = Stopwatch.StartNew();
        var summaries = WadCatalog.Scan(dataRoot).ToList();
        if (clientRoot is not null)
        {
            Console.WriteLine(Strings.T("Cli.Scan.ClientScanning", clientRoot));
            summaries.AddRange(WadCatalog.ScanClient(clientRoot));
        }
        timer.Stop();

        Console.WriteLine($"{Strings.T("Cli.Scan.ColumnWad"),-42}{Strings.T("Cli.Scan.ColumnEntries"),8}" +
                          $"{Strings.T("Cli.Scan.ColumnUnpacked"),12}  {Strings.T("Cli.Scan.ColumnStorage")}");
        foreach (var summary in summaries.OrderByDescending(s => s.EntryCount))
        {
            Console.WriteLine($"{Truncate(summary.RelativePath, 42),-42}{summary.EntryCount,8}{ExtractionSummary.Format(summary.UncompressedSize),12}  {Truncate(summary.StorageSummary, 40)}");
        }
        Console.WriteLine(Strings.T("Cli.Scan.Summary", summaries.Count, summaries.Sum(s => s.EntryCount), timer.Elapsed.TotalSeconds));

        var jsonPath = parsed.Get("json");
        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(summaries, JsonOptions));
            Console.WriteLine(Strings.T("Cli.Json.Written", jsonPath));
        }
        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static int List(AppSettings settings, CommandLine parsed)
    {
        var dataRoot = ResolveRoot(settings, parsed, out var clientRoot);
        if (dataRoot is null) return 1;
        var pattern = parsed.Positionals.FirstOrDefault() ?? parsed.Get("wad") ?? "*";
        var wads = MatchWads(dataRoot, clientRoot, pattern);
        if (wads.Count == 0)
        {
            Console.Error.WriteLine(Strings.T("Cli.List.NoMatch", pattern));
            return 1;
        }

        var names = LoadNames(settings);
        var filter = BuildFilter(parsed);
        var limit = parsed.GetInt("limit") ?? 200;

        var rows = new List<object>();
        foreach (var wad in wads)
        {
            using var archive = WadArchive.Open(wad);
            var entries = archive.ReadEntries(names).Where(filter.Matches).ToList();
            Console.WriteLine(Strings.T("Cli.List.WadHeader", Path.GetFileName(wad), entries.Count, archive.EntryCount));
            foreach (var entry in entries.Take(limit))
            {
                Console.WriteLine($"  {PathHashing.ToText(entry.Hash)} {entry.UncompressedSize,10} {(entry.IsNamed ? entry.Path : "?")}");
                rows.Add(new { wad = WadKey(wad), hash = PathHashing.ToText(entry.Hash), size = entry.UncompressedSize, path = entry.Path, ext = entry.Extension, category = entry.Category });
            }
            if (entries.Count > limit) Console.WriteLine(Strings.T("Cli.List.More", entries.Count - limit));
        }

        var jsonPath = parsed.Get("json");
        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(rows, JsonOptions));
            Console.WriteLine(Strings.T("Cli.Json.Written", jsonPath));
        }
        return 0;
    }

    /// <summary>
    /// <c>extract</c> honours the format flags; <paramref name="mediaOnly"/> (<c>media</c>) keeps only
    /// the second-pass media: textures as png, audio as mp3.
    /// </summary>
    private static int Extract(AppSettings settings, CommandLine parsed, bool mediaOnly = false)
    {
        var dataRoot = ResolveRoot(settings, parsed, out var clientRoot);
        if (dataRoot is null) return 1;

        var output = parsed.Get("out") ?? settings.OutputRoot;
        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine(Strings.T("Cli.Extract.OutputMissing"));
            return 1;
        }

        var wadPattern = parsed.Get("wad") ?? "*";
        var wads = MatchWads(dataRoot, clientRoot, wadPattern);
        if (wads.Count == 0)
        {
            Console.Error.WriteLine(Strings.T("Cli.List.NoMatch", wadPattern));
            return 1;
        }

        var names = LoadNames(settings);
        var filter = BuildFilter(parsed);
        if (mediaOnly) filter.Categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "texture", "audio" };
        var options = new ExtractionOptions
        {
            OutputRoot = Path.GetFullPath(output),
            FlatLayout = parsed.Has("flat"),
            SkipExisting = !parsed.Has("overwrite"),
            Threads = parsed.GetInt("threads") ?? Math.Clamp(Environment.ProcessorCount - 1, 1, 16),
            WriteManifest = !parsed.Has("no-manifest"),
            MediaOnly = mediaOnly,
            ConvertTextures = mediaOnly || !parsed.Has("raw"),
            AudioFormat = mediaOnly ? AudioExportFormat.Mp3 : ParseAudioFormat(parsed.Get("audio")),
        };

        Console.WriteLine(mediaOnly
            ? Strings.T("Cli.Extract.PreparingMedia", wads.Count, options.OutputRoot, options.Threads)
            : Strings.T("Cli.Extract.Preparing", wads.Count, options.OutputRoot, options.Threads));
        if (parsed.Has("dry-run"))
        {
            foreach (var wad in wads)
            {
                using var archive = WadArchive.Open(wad);
                var entries = archive.ReadEntries(names).Where(filter.Matches).ToList();
                Console.WriteLine(Strings.T("Cli.Extract.DryRunLine", Path.GetFileName(wad), entries.Count,
                    ExtractionSummary.Format(entries.Sum(e => e.UncompressedSize))));
            }
            return 0;
        }

        var lastReport = DateTime.UtcNow;
        var progress = new InlineProgress<ExtractionProgress>(p =>
        {
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 200 && p.FilesDone != p.FilesTotal) return;
            lastReport = DateTime.UtcNow;
            var percent = p.FilesTotal == 0 ? 100 : p.FilesDone * 100.0 / p.FilesTotal;
            Console.Write($"\r{Strings.T("Cli.Extract.Progress", percent, p.FilesDone, p.FilesTotal, p.Extracted, p.Skipped, ExtractionSummary.Format(p.BytesDone), Truncate(p.CurrentFile ?? string.Empty, 50))}   ");
        });

        var summary = ExtractionService.Extract(dataRoot, wads, names, filter, options, progress, Console.WriteLine);
        Console.WriteLine();
        Console.WriteLine(summary.Describe());
        if (summary.ManifestPath is not null) Console.WriteLine(Strings.T("Cli.Extract.Manifest", summary.ManifestPath));
        foreach (var error in summary.Errors) Console.Error.WriteLine(Strings.T("Cli.Extract.Failed", error));
        return summary.Failed == 0 ? 0 : 3;
    }

    /// <summary>
    /// Builds the classified catalog and prints what landed in each section. This is the headless
    /// check of the classification rules: the numbers here are what the tree shows.
    /// </summary>
    private static int Catalog(AppSettings settings, CommandLine parsed)
    {
        var dataRoot = ResolveRoot(settings, parsed, out var clientRoot);
        if (dataRoot is null) return 1;

        var names = LoadNames(settings);
        var clientNames = LoadClientNames(settings);

        var timer = Stopwatch.StartNew();
        var summaries = WadCatalog.Scan(dataRoot).ToList();
        if (clientRoot is not null) summaries.AddRange(WadCatalog.ScanClient(clientRoot));
        Console.WriteLine(Strings.T("Cli.Catalog.Scanning", summaries.Count, summaries.Sum(s => s.EntryCount), timer.Elapsed.TotalSeconds));

        timer.Restart();
        var tables = CatalogTables.LoadFromClient(summaries.Where(summary => summary.IsClient).ToList(), clientNames, new InlineProgress<string>(Console.WriteLine));
        var catalog = AssetCatalog.Build(summaries, names, null, tables,
            new InlineProgress<string>(message => Console.WriteLine("  " + message)));
        timer.Stop();
        Console.WriteLine(Strings.T("Cli.Catalog.Built", catalog.Count, catalog.Groups.Count(group => group.Count > 0), timer.Elapsed.TotalSeconds));

        var bucket = parsed.Get("bucket");
        var limit = parsed.GetInt("limit") ?? 15;
        var showAll = string.IsNullOrWhiteSpace(bucket);
        CatalogSection? only = null;
        if (!showAll)
        {
            if (CatalogSectionInfo.TryParse(bucket, out var wanted)) only = wanted;
            else
                Console.WriteLine(Strings.T("Cli.Catalog.NoSection", bucket,
                    string.Join(" / ", CatalogSectionInfo.All.Select(info => info.Display))));
        }

        foreach (var info in CatalogSectionInfo.All)
        {
            if (only is { } filter && filter != info.Section) continue;

            var count = catalog.CountOf(info.Section);
            var groups = 0;
            var entities = 0;
            for (var i = 0; i < catalog.Groups.Count; i++)
            {
                var group = catalog.Groups[i];
                if (group.Section != info.Section || group.Count == 0) continue;
                groups++;
                entities += catalog.EntitiesOf(i).Count;
            }

            Console.WriteLine(Strings.T("Cli.Catalog.SectionLine", info.Display, count, groups, entities, info.Description));
        }

        // --search runs the same blob search the UI does, so a miss can be reproduced here.
        if (parsed.Get("search") is { Length: > 0 } search)
        {
            var watch = Stopwatch.StartNew();
            var hits = catalog.Search(search, limit);
            Console.WriteLine();
            Console.WriteLine(Strings.T("Cli.Catalog.SearchResult", search, hits.Total, watch.ElapsedMilliseconds));
            foreach (var entry in hits.Entries.Take(limit))
                Console.WriteLine($"  {CatalogSectionInfo.Of(entry.Section).Display}  {entry.Path}");
        }

        if (only is { } section)
        {
            Console.WriteLine();
            Console.WriteLine(Strings.T("Cli.Catalog.SectionGroups", CatalogSectionInfo.Of(section).Display, limit));
            foreach (var group in catalog.GroupsOf(section).OrderByDescending(group => group.Count).Take(limit))
                Console.WriteLine($"{group.Count,8:N0}  {Truncate(group.Label, 60)}");
        }

        Console.WriteLine();
        Console.WriteLine(Strings.T("Cli.Catalog.TopGroups", limit));
        foreach (var group in catalog.Groups.Where(group => group.Count > 0).OrderByDescending(group => group.Count).Take(limit))
            Console.WriteLine($"{group.Count,8:N0}  {CatalogSectionInfo.Of(group.Section).Display} · {Truncate(group.Label, 60)}");

        var jsonPath = parsed.Get("json");
        if (jsonPath is not null)
        {
            var details = CatalogSectionInfo.All.ToDictionary(
                info => info.Display,
                info => catalog.EntriesOf(info.Section).Take(200000).Select(entry => new
                {
                    path = entry.Path,
                    group = catalog.Groups[entry.Group].Label,
                    entity = catalog.Entities[entry.Entity].Label,
                    wad = Path.GetFileName(entry.WadPath),
                    copies = entry.WadCount,
                    size = entry.Size,
                }).ToArray());
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(details, JsonOptions));
            Console.WriteLine(Strings.T("Cli.Json.Written", jsonPath));
        }

        return 0;
    }

    private static int Hashes(AppSettings settings, CommandLine parsed)
    {
        var action = parsed.Positionals.FirstOrDefault()?.ToLowerInvariant() ?? "stats";
        var directory = parsed.Get("dir") ?? settings.HashTableDirectory;
        var index = parsed.Get("out") ?? settings.NamesIndexPath;

        switch (action)
        {
            case "fetch":
            {
                var fetcher = new HashTableFetcher();
                var progress = new InlineProgress<HashTableFetchProgress>(p =>
                {
                    var percent = p.BytesTotal == 0 ? 0 : p.BytesDone * 100.0 / p.BytesTotal;
                    Console.Write($"\r{Strings.T("Cli.Hashes.Progress", p.Phase, p.FilesDone, p.FilesTotal, percent, ExtractionSummary.Format(p.BytesDone), ExtractionSummary.Format(p.BytesTotal))}   ");
                });
                var result = fetcher.FetchAsync(directory, parsed.Get("token"), progress).GetAwaiter().GetResult();
                Console.WriteLine();
                Console.WriteLine(Strings.T("Cli.Hashes.Ready", result.Downloaded, result.Skipped, ExtractionSummary.Format(result.Bytes), directory));

                // 客户端插件包用另一张表（插件路径），单独下载、单独建索引
                try
                {
                    var clientDirectory = Path.Combine(directory, "client");
                    var clientText = fetcher.FetchClientTableAsync(clientDirectory, parsed.Get("token"), progress).GetAwaiter().GetResult();
                    var clientTable = NameTable.LoadTextFiles(new[] { clientText });
                    var clientIndex = Path.Combine(clientDirectory, "client-names.idx");
                    clientTable.SaveBinary(clientIndex);
                    Console.WriteLine(Strings.T("Cli.Hashes.ClientIndex", clientTable.Count, clientIndex));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Strings.T("Cli.Hashes.ClientFailed", ex.Message));
                }

                if (parsed.Has("build"))
                {
                    settings.DataDirectory = Directory.GetParent(directory)?.FullName ?? settings.DataDirectory;
                    return BuildIndex(settings, parsed, directory, index, scope: true);
                }
                return 0;
            }
            case "build":
                return BuildIndex(settings, parsed, directory, index, scope: parsed.Has("scope"));
            case "stats":
            {
                var files = HashTableFetcher.FindTextTables(directory);
                Console.WriteLine(Strings.T("Cli.Hashes.Directory", directory, files.Count,
                    ExtractionSummary.Format(files.Sum(f => new FileInfo(f).Length))));
                if (File.Exists(index))
                {
                    var table = NameTable.LoadBinary(index);
                    Console.WriteLine(Strings.T("Cli.Hashes.Index", index, table.Count,
                        ExtractionSummary.Format(new FileInfo(index).Length)));
                }
                else Console.WriteLine(Strings.T("Cli.Hashes.NoIndex"));
                return 0;
            }
            default:
                Console.Error.WriteLine(Strings.T("Cli.Hashes.UnknownAction", action));
                return 2;
        }
    }

    private static int BuildIndex(AppSettings settings, CommandLine parsed, string directory, string index, bool scope)
    {
        var textFiles = HashTableFetcher.FindTextTables(directory);
        if (textFiles.Count == 0)
        {
            Console.Error.WriteLine(Strings.T("Cli.Index.NoTables", directory));
            return 1;
        }

        var timer = Stopwatch.StartNew();
        var table = NameTable.LoadTextFiles(textFiles, new InlineProgress<string>(Console.WriteLine));
        Console.WriteLine(Strings.T("Cli.Index.Loaded", table.Count, timer.Elapsed.TotalSeconds));

        if (scope)
        {
            var dataRoot = ResolveRoot(settings, parsed, out _);
            if (dataRoot is not null)
            {
                var hashes = new HashSet<ulong>();
                foreach (var wad in LolInstall.EnumerateWadFiles(dataRoot))
                {
                    using var archive = WadArchive.Open(wad);
                    foreach (var entry in archive.ReadEntries(null)) hashes.Add(entry.Hash);
                }
                var scoped = table.Filter(hashes);
                Console.WriteLine(Strings.T("Cli.Index.Scoped", hashes.Count, scoped.Count, 100.0 * scoped.Count / hashes.Count));
                table = scoped;
            }
        }

        table.SaveBinary(index);
        Console.WriteLine(Strings.T("Cli.Index.Written", index, table.Count, ExtractionSummary.Format(new FileInfo(index).Length), timer.Elapsed.TotalSeconds));
        return 0;
    }

    private static IReadOnlyList<string> MatchWads(string dataRoot, string? clientRoot, string pattern)
    {
        // 不含通配符的名称按“包含”匹配，方便直接写 Ahri 而不是 Ahri.wad.client
        var regex = ExtractionFilter.GlobToRegex(pattern.Contains('*') || pattern.Contains('?') ? pattern : $"*{pattern}*");
        var game = LolInstall.EnumerateWadFiles(dataRoot)
            .Where(path => regex.IsMatch(Path.GetFileName(path)) || regex.IsMatch(LolInstall.GetRelativeName(dataRoot, path)));

        if (clientRoot is null) return game.ToArray();

        var client = LolClient.EnumerateWadFiles(clientRoot)
            .Where(path => regex.IsMatch(Path.GetFileName(path)) || regex.IsMatch(LolClient.RelativePath(clientRoot, path)));
        return game.Concat(client).ToArray();
    }

    private static ExtractionFilter BuildFilter(CommandLine parsed)
    {
        var filter = new ExtractionFilter
        {
            PathPattern = parsed.Get("filter"),
            PathPrefix = parsed.Get("path"),
            MinSize = parsed.GetLong("min-size"),
            MaxSize = parsed.GetLong("max-size"),
            IncludeUnnamed = !parsed.Has("no-unnamed"),
        };

        var categories = parsed.Get("category");
        if (!string.IsNullOrWhiteSpace(categories))
            filter.Categories = SplitList(categories);

        var extensions = parsed.Get("ext");
        if (!string.IsNullOrWhiteSpace(extensions))
            filter.Extensions = SplitList(extensions);

        return filter;
    }

    /// <summary><c>--audio keep|wav|mp3</c>, mp3 by default.</summary>
    private static AudioExportFormat ParseAudioFormat(string? value) => (value ?? "mp3").ToLowerInvariant() switch
    {
        "keep" or "raw" or "none" => AudioExportFormat.Keep,
        "wav" => AudioExportFormat.Wav,
        _ => AudioExportFormat.Mp3,
    };

    private static HashSet<string> SplitList(string value)
        => value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string WadKey(string wadPath) => ExtractionService.WadKeyOf(wadPath);

    private static string Truncate(string text, int length)
        => text.Length <= length ? text : text[..(length - 1)] + "…";
}

internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positionals { get; } = new();

    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                string? value = null;
                var equals = name.IndexOf('=');
                if (equals >= 0)
                {
                    value = name[(equals + 1)..];
                    name = name[..equals];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                result._options[name] = value ?? "true";
            }
            else
            {
                result.Positionals.Add(arg);
            }
        }
        return result;
    }

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => _options.ContainsKey(name);

    public int? GetInt(string name) => int.TryParse(Get(name), out var value) ? value : null;

    public long? GetLong(string name) => long.TryParse(Get(name), out var value) ? value : null;
}

/// <summary>控制台下同步回调，避免 Progress&lt;T&gt; 把输出顺序打乱。</summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
