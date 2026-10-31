using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LolDataExtractor.Core;

public sealed record HashTableFile(string Name, long Size);

public sealed record HashTableFetchProgress(
    string Phase,
    string? File,
    long BytesDone,
    long BytesTotal,
    int FilesDone,
    int FilesTotal);

public sealed record HashTableFetchResult(
    IReadOnlyList<string> Files,
    int Downloaded,
    int Skipped,
    long Bytes);

/// <summary>
/// Downloads the CommunityDragon hash tables (hash to asset path) that give wad
/// entries their names. Files are fetched through the GitHub contents API because
/// raw.githubusercontent.com is frequently unreachable behind mainland networks.
/// </summary>
public sealed class HashTableFetcher
{
    public const string RepositoryOwner = "CommunityDragon";
    public const string RepositoryName = "Data";
    public const string HashesDirectory = "hashes/lol";
    public const string GamePrefix = "hashes.game.txt";

    /// <summary>The client (LCU) name table; entries below <c>plugins/&lt;plugin&gt;/…</c>.</summary>
    public const string ClientTableName = "hashes.lcu.txt";

    private readonly HttpClient _http;

    public HashTableFetcher(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LolDataExtractor", "1.0"));
    }

    public static string? ResolveToken(string? explicitToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken.Trim();
        foreach (var name in new[] { "GITHUB_TOKEN", "GitHub_token", "GH_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Every text hash table in a hash table directory, ordered by part number: the downloaded
    /// <c>hashes.game.txt.0</c>, <c>.1</c> … parts plus any <c>*.txt</c> table the user dropped in
    /// by hand. The parts end in a digit, not in <c>.txt</c>, so a <c>*.txt</c> glob finds none of
    /// them and matching has to be done on the name itself.
    /// </summary>
    public static IReadOnlyList<string> FindTextTables(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();

        return Directory.EnumerateFiles(directory)
            .Where(path => IsTextTable(Path.GetFileName(path)))
            .OrderBy(PartNumber)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTextTable(string name) =>
        name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith(GamePrefix + ".", StringComparison.Ordinal) && PartNumber(name) >= 0);

    /// <summary>The numeric suffix of a part file; "-1" for a name without one.</summary>
    private static int PartNumber(string path)
    {
        var name = Path.GetFileName(path);
        var dot = name.LastIndexOf('.');
        return dot >= 0 && int.TryParse(name[(dot + 1)..], out var number) ? number : -1;
    }

    private static Uri DirectoryUri => new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/contents/{HashesDirectory}");

    private HttpRequestMessage CreateRequest(Uri uri, bool raw, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(raw ? "application/vnd.github.raw" : "application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<IReadOnlyList<HashTableFile>> ListGamePartsAsync(string? token = null, CancellationToken cancellationToken = default)
    {
        var files = await ListDirectoryAsync(token, cancellationToken).ConfigureAwait(false);
        return files.Where(file => file.Name.StartsWith(GamePrefix, StringComparison.Ordinal))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Lists the hash table folder; every file, so callers can pick the game parts or the client table.</summary>
    private async Task<List<HashTableFile>> ListDirectoryAsync(string? token, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(CreateRequest(DirectoryUri, raw: false, token), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("远端哈希表目录不存在，请检查网络或仓库状态");
        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            throw new InvalidOperationException("GitHub API 访问频率受限，请稍后再试或配置 GitHub Token");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var files = new List<HashTableFile>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = element.GetProperty("name").GetString();
            if (name is null) continue;
            files.Add(new HashTableFile(name, element.GetProperty("size").GetInt64()));
        }
        return files;
    }

    /// <summary>
    /// Downloads <c>hashes.lcu.txt</c>, the name table of the client plugin archives. It is a single
    /// 17 MB file, so it is fetched (not listed part by part) unless an identical copy is present.
    /// </summary>
    public async Task<string> FetchClientTableAsync(
        string targetDirectory,
        string? token = null,
        IProgress<HashTableFetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        token = ResolveToken(token);
        Directory.CreateDirectory(targetDirectory);

        var files = await ListDirectoryAsync(token, cancellationToken).ConfigureAwait(false);
        var file = files.FirstOrDefault(f => f.Name == ClientTableName)
                   ?? throw new InvalidOperationException($"远端没有提供 {ClientTableName}");
        var target = Path.Combine(targetDirectory, file.Name);

        if (File.Exists(target) && new FileInfo(target).Length == file.Size)
        {
            progress?.Report(new HashTableFetchProgress("skip", file.Name, file.Size, file.Size, 1, 1));
            return target;
        }

        var uri = new Uri($"{DirectoryUri}/{file.Name}");
        var temp = target + ".download";
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await DownloadPartAsync(uri, temp, token, file, 0, file.Size, 0, 1, progress, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new HashTableFetchProgress($"重试 {attempt}", file.Name, 0, file.Size, 0, 1));
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(temp, target, overwrite: true);
        progress?.Report(new HashTableFetchProgress("done", file.Name, file.Size, file.Size, 1, 1));
        return target;
    }

    /// <summary>Downloads every game hashtable part that is missing or truncated.</summary>
    public async Task<HashTableFetchResult> FetchAsync(
        string targetDirectory,
        string? token = null,
        IProgress<HashTableFetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        token = ResolveToken(token);
        Directory.CreateDirectory(targetDirectory);

        var parts = await ListGamePartsAsync(token, cancellationToken).ConfigureAwait(false);
        if (parts.Count == 0) throw new InvalidOperationException("远端未提供任何哈希表分片");

        var totalBytes = parts.Sum(p => p.Size);
        var downloaded = 0;
        var skipped = 0;
        var files = new List<string>();
        var completedBytes = 0L;

        foreach (var part in parts)
        {
            var target = Path.Combine(targetDirectory, part.Name);
            files.Add(target);

            if (File.Exists(target) && new FileInfo(target).Length == part.Size)
            {
                skipped++;
                completedBytes += part.Size;
                progress?.Report(new HashTableFetchProgress("skip", part.Name, completedBytes, totalBytes, skipped + downloaded, parts.Count));
                continue;
            }

            var uri = new Uri($"{DirectoryUri}/{part.Name}");
            var temp = target + ".download";
            var attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    await DownloadPartAsync(uri, temp, token, part, completedBytes, totalBytes, skipped + downloaded, parts.Count, progress, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
                {
                    progress?.Report(new HashTableFetchProgress($"重试 {attempt}", part.Name, completedBytes, totalBytes, skipped + downloaded, parts.Count));
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temp, target, overwrite: true);
            downloaded++;
            completedBytes += part.Size;
            progress?.Report(new HashTableFetchProgress("done", part.Name, completedBytes, totalBytes, skipped + downloaded, parts.Count));
        }

        return new HashTableFetchResult(files, downloaded, skipped, totalBytes);
    }

    private const int MaxAttempts = 4;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private async Task DownloadPartAsync(
        Uri uri,
        string temp,
        string? token,
        HashTableFile part,
        long completedBytes,
        long totalBytes,
        int filesDone,
        int filesTotal,
        IProgress<HashTableFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(CreateRequest(uri, raw: true, token), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            throw new InvalidOperationException("GitHub API 访问频率受限，请稍后再试或配置 GitHub Token");
        response.EnsureSuccessStatusCode();

        long written = 0;
        var lastReport = DateTime.UtcNow - ProgressInterval;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;

            // 限流上报，避免 200MB 下载把界面事件队列打满
            if (DateTime.UtcNow - lastReport >= ProgressInterval)
            {
                lastReport = DateTime.UtcNow;
                progress?.Report(new HashTableFetchProgress("download", part.Name, completedBytes + written, totalBytes, filesDone, filesTotal));
            }
        }

        if (written != part.Size)
            throw new IOException($"{part.Name} 下载不完整（{written}/{part.Size} 字节）");
    }
}
