using System.Diagnostics;
using System.Text;
using LolDataExtractor.Core;

namespace LolDataExtractor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
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
            "hashes" => Hashes(settings, parsed),
            _ => Unknown(command),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            英雄联盟数据提取工具 (loldata)

            用法: loldata <命令> [选项]

              info                            显示安装目录、哈希表与名称索引状态
              scan   [--root 路径] [--json 文件]
                                              扫描全部 wad，输出条目数量与压缩方式
              list   <wad 名称或通配符> [--root 路径] [--filter 通配符] [--category 类别]
                     [--ext 扩展名] [--path 目录前缀] [--limit N] [--json 文件]
                                              列出某个 wad（或匹配的一组 wad）的条目
              extract [--root 路径] [--wad 通配符] [--out 目录] [--filter 通配符]
                     [--category 类别] [--ext 扩展名] [--path 目录前缀] [--min-size 字节] [--max-size 字节]
                     [--threads N] [--flat] [--overwrite] [--dry-run] [--no-unnamed]
                     [--audio mp3|wav|keep] [--raw]
                                              批量导出条目（默认跳过已导出过的文件）
                                              贴图默认转 PNG，音频默认转 MP3；--audio keep 或
                                              --raw 保留 wad 里的原始格式
              media  [--root 路径] [--wad 通配符] [--out 目录] [--filter 通配符]
                     [--path 目录前缀] [--flat] [--overwrite] [--dry-run]
                                              只导出二次解析后的媒体：贴图转 PNG，音频转 MP3
                                              其余条目（模型、数据等）不导出
              hashes fetch [--token T] [--dir 目录] [--build]
                                              下载 CommunityDragon 哈希表（约 217MB，9 个分片）
              hashes build [--dir 目录] [--out 文件] [--root 路径 --scope]
                                              由文本哈希表生成二进制名称索引（--scope 只保留本地 wad 用到的项）
              hashes stats                    统计哈希表与索引规模

            公共选项: --data-dir 目录   覆盖工具数据目录（存放哈希表与索引）
                      --root 路径       游戏 DATA 目录（默认读取上一次使用的配置）

            类别取值: texture, model, audio, bin, text, shader, other
            """);
    }

    private static string? ResolveRoot(AppSettings settings, CommandLine parsed)
    {
        var root = parsed.Get("root") ?? settings.DataRoot;
        var resolved = LolInstall.TryLocate(root);
        if (resolved is null)
        {
            Console.Error.WriteLine($"未能识别游戏目录: {root ?? "(未设置)"}");
            Console.Error.WriteLine("请用 --root 指定，例如 --root \"D:\\WeGameApps\\英雄联盟（含经典模式）\\Game\\DATA\"");
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
        foreach (var path in new[] { settings.NamesIndexPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                return NameTable.LoadBinary(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"名称索引读取失败: {ex.Message}");
            }
        }

        var textFiles = HashTableFetcher.FindTextTables(settings.HashTableDirectory);
        if (textFiles.Count > 0)
        {
            Console.WriteLine($"正在解析文本哈希表（{textFiles.Count} 个文件）…");
            var table = NameTable.LoadTextFiles(textFiles);
            Console.WriteLine($"已载入 {table.Count} 条路径");
            return table;
        }

        Console.WriteLine("提示: 未找到名称索引，条目将以哈希命名。请先运行 loldata hashes fetch --build");
        return NameTable.Empty;
    }

    private static int Info(AppSettings settings, CommandLine parsed)
    {
        var dataRoot = LolInstall.TryLocate(parsed.Get("root") ?? settings.DataRoot);
        Console.WriteLine($"英雄联盟数据提取工具 {AppInfo.Version}（{AppInfo.ReleaseDate} 发布）");
        Console.WriteLine($"游戏 DATA 目录 : {dataRoot ?? "(未找到)"}");
        Console.WriteLine($"输出目录       : {settings.OutputRoot}");
        Console.WriteLine($"工具数据目录   : {settings.DataDirectory}");
        Console.WriteLine($"哈希表目录     : {settings.HashTableDirectory}");
        Console.WriteLine($"名称索引       : {settings.NamesIndexPath} {(File.Exists(settings.NamesIndexPath) ? "(已生成)" : "(未生成)")}");

        if (dataRoot is not null)
        {
            var wads = LolInstall.EnumerateWadFiles(dataRoot);
            Console.WriteLine($"wad 数量       : {wads.Count}");
            var totalSize = wads.Sum(w => new FileInfo(w).Length);
            Console.WriteLine($"wad 总大小     : {ExtractionSummary.Format(totalSize)}");
        }

        if (File.Exists(settings.NamesIndexPath))
        {
            var names = NameTable.LoadBinary(settings.NamesIndexPath);
            Console.WriteLine($"名称索引条目   : {names.Count:N0}");
        }

        return 0;
    }

    private static int Scan(AppSettings settings, CommandLine parsed)
    {
        var dataRoot = ResolveRoot(settings, parsed);
        if (dataRoot is null) return 1;

        Console.WriteLine($"扫描 {dataRoot} …");
        var timer = Stopwatch.StartNew();
        var summaries = WadCatalog.Scan(dataRoot);
        timer.Stop();

        Console.WriteLine($"{"wad",-42}{"条目",8}{"展开大小",12}  压缩方式");
        foreach (var summary in summaries.OrderByDescending(s => s.EntryCount))
        {
            Console.WriteLine($"{Truncate(summary.RelativePath, 42),-42}{summary.EntryCount,8}{ExtractionSummary.Format(summary.UncompressedSize),12}  {Truncate(summary.StorageSummary, 40)}");
        }
        Console.WriteLine($"共 {summaries.Count} 个 wad，{summaries.Sum(s => s.EntryCount):N0} 个条目，用时 {timer.Elapsed.TotalSeconds:F1}s");

        var jsonPath = parsed.Get("json");
        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(summaries, JsonOptions));
            Console.WriteLine($"已写入 {jsonPath}");
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
        var dataRoot = ResolveRoot(settings, parsed);
        if (dataRoot is null) return 1;
        var pattern = parsed.Positionals.FirstOrDefault() ?? parsed.Get("wad") ?? "*";
        var wads = MatchWads(dataRoot, pattern);
        if (wads.Count == 0)
        {
            Console.Error.WriteLine($"没有匹配 {pattern} 的 wad");
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
            Console.WriteLine($"== {Path.GetFileName(wad)} ({entries.Count}/{archive.EntryCount} 个条目)");
            foreach (var entry in entries.Take(limit))
            {
                Console.WriteLine($"  {PathHashing.ToText(entry.Hash)} {entry.UncompressedSize,10} {(entry.IsNamed ? entry.Path : "?")}");
                rows.Add(new { wad = WadKey(wad), hash = PathHashing.ToText(entry.Hash), size = entry.UncompressedSize, path = entry.Path, ext = entry.Extension, category = entry.Category });
            }
            if (entries.Count > limit) Console.WriteLine($"  … 另有 {entries.Count - limit} 条，使用 --limit 调整");
        }

        var jsonPath = parsed.Get("json");
        if (jsonPath is not null)
        {
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(rows, JsonOptions));
            Console.WriteLine($"已写入 {jsonPath}");
        }
        return 0;
    }

    /// <summary>
    /// <c>extract</c> honours the format flags; <paramref name="mediaOnly"/> (<c>media</c>) keeps only
    /// the second-pass media: textures as png, audio as mp3.
    /// </summary>
    private static int Extract(AppSettings settings, CommandLine parsed, bool mediaOnly = false)
    {
        var dataRoot = ResolveRoot(settings, parsed);
        if (dataRoot is null) return 1;

        var output = parsed.Get("out") ?? settings.OutputRoot;
        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("请用 --out 指定导出目录");
            return 1;
        }

        var wadPattern = parsed.Get("wad") ?? "*";
        var wads = MatchWads(dataRoot, wadPattern);
        if (wads.Count == 0)
        {
            Console.Error.WriteLine($"没有匹配 {wadPattern} 的 wad");
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
            ? $"准备只导出媒体（PNG + MP3）{wads.Count} 个 wad → {options.OutputRoot}（线程 {options.Threads}）"
            : $"准备导出 {wads.Count} 个 wad → {options.OutputRoot}（线程 {options.Threads}）");
        if (parsed.Has("dry-run"))
        {
            foreach (var wad in wads)
            {
                using var archive = WadArchive.Open(wad);
                var entries = archive.ReadEntries(names).Where(filter.Matches).ToList();
                Console.WriteLine($"{Path.GetFileName(wad),-40} {entries.Count,7} 个条目 {ExtractionSummary.Format(entries.Sum(e => e.UncompressedSize)),12}");
            }
            return 0;
        }

        var lastReport = DateTime.UtcNow;
        var progress = new InlineProgress<ExtractionProgress>(p =>
        {
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 200 && p.FilesDone != p.FilesTotal) return;
            lastReport = DateTime.UtcNow;
            var percent = p.FilesTotal == 0 ? 100 : p.FilesDone * 100.0 / p.FilesTotal;
            Console.Write($"\r[{percent,5:F1}%] {p.FilesDone}/{p.FilesTotal} 导出 {p.Extracted} 跳过 {p.Skipped} {ExtractionSummary.Format(p.BytesDone)} {Truncate(p.CurrentFile ?? string.Empty, 50)}   ");
        });

        var summary = ExtractionService.Extract(dataRoot, wads, names, filter, options, progress, Console.WriteLine);
        Console.WriteLine();
        Console.WriteLine(summary.Describe());
        if (summary.ManifestPath is not null) Console.WriteLine($"清单: {summary.ManifestPath}");
        foreach (var error in summary.Errors) Console.Error.WriteLine("  失败: " + error);
        return summary.Failed == 0 ? 0 : 3;
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
                    Console.Write($"\r{p.Phase,-8} {p.FilesDone}/{p.FilesTotal} 文件 {percent,5:F1}% {ExtractionSummary.Format(p.BytesDone)}/{ExtractionSummary.Format(p.BytesTotal)}   ");
                });
                var result = fetcher.FetchAsync(directory, parsed.Get("token"), progress).GetAwaiter().GetResult();
                Console.WriteLine();
                Console.WriteLine($"哈希表就绪: 下载 {result.Downloaded} 个、跳过 {result.Skipped} 个，共 {ExtractionSummary.Format(result.Bytes)}，目录 {directory}");
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
                Console.WriteLine($"哈希表目录 {directory}: {files.Count} 个文件，{ExtractionSummary.Format(files.Sum(f => new FileInfo(f).Length))}");
                if (File.Exists(index))
                {
                    var table = NameTable.LoadBinary(index);
                    Console.WriteLine($"名称索引 {index}: {table.Count:N0} 条，{ExtractionSummary.Format(new FileInfo(index).Length)}");
                }
                else Console.WriteLine("名称索引未生成");
                return 0;
            }
            default:
                Console.Error.WriteLine($"未知 hashes 子命令: {action}（可用: fetch, build, stats）");
                return 2;
        }
    }

    private static int BuildIndex(AppSettings settings, CommandLine parsed, string directory, string index, bool scope)
    {
        var textFiles = HashTableFetcher.FindTextTables(directory);
        if (textFiles.Count == 0)
        {
            Console.Error.WriteLine($"目录 {directory} 中没有哈希表文本文件，请先运行 loldata hashes fetch");
            return 1;
        }

        var timer = Stopwatch.StartNew();
        var table = NameTable.LoadTextFiles(textFiles, new InlineProgress<string>(Console.WriteLine));
        Console.WriteLine($"载入 {table.Count:N0} 条路径，用时 {timer.Elapsed.TotalSeconds:F1}s");

        if (scope)
        {
            var dataRoot = ResolveRoot(settings, parsed);
            if (dataRoot is not null)
            {
                var hashes = new HashSet<ulong>();
                foreach (var wad in LolInstall.EnumerateWadFiles(dataRoot))
                {
                    using var archive = WadArchive.Open(wad);
                    foreach (var entry in archive.ReadEntries(null)) hashes.Add(entry.Hash);
                }
                var scoped = table.Filter(hashes);
                Console.WriteLine($"本地 wad 需要 {hashes.Count:N0} 个 hash，命中 {scoped.Count:N0} 条路径（{100.0 * scoped.Count / hashes.Count:F1}%）");
                table = scoped;
            }
        }

        table.SaveBinary(index);
        Console.WriteLine($"名称索引已写入 {index}（{table.Count:N0} 条，{ExtractionSummary.Format(new FileInfo(index).Length)}），用时 {timer.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    private static IReadOnlyList<string> MatchWads(string dataRoot, string pattern)
    {
        // 不含通配符的名称按“包含”匹配，方便直接写 Ahri 而不是 Ahri.wad.client
        var regex = ExtractionFilter.GlobToRegex(pattern.Contains('*') || pattern.Contains('?') ? pattern : $"*{pattern}*");
        return LolInstall.EnumerateWadFiles(dataRoot)
            .Where(path => regex.IsMatch(Path.GetFileName(path)) || regex.IsMatch(LolInstall.GetRelativeName(dataRoot, path)))
            .ToArray();
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
