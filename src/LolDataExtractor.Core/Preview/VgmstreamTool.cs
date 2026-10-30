using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace LolDataExtractor.Core;

/// <summary>
/// Wwise streams (fmt tag 0xFFFF) are neither PCM nor plain Vorbis, so WPF and NAudio
/// cannot open them directly. vgmstream decodes them; this helper downloads the official
/// Windows build on demand and runs the command line tool.
/// </summary>
public static class VgmstreamTool
{
    public const string DownloadUrl = "https://github.com/vgmstream/vgmstream/releases/download/r2168/vgmstream-win64.zip";

    private const string ExecutableName = "vgmstream-cli.exe";

    /// <summary>Where the tool is unpacked, below the shared tool data directory.</summary>
    public static string InstallDirectory => Path.Combine(AppSettings.DefaultDataDirectory(), "tools", "vgmstream");

    /// <summary>Temp folder for transcoded previews.</summary>
    public static string CacheDirectory => Path.Combine(AppSettings.DefaultDataDirectory(), "preview-cache");

    /// <summary>Full path of a usable vgmstream-cli.exe, or null when it is not installed.</summary>
    public static string? FindExecutable()
    {
        if (!Directory.Exists(InstallDirectory)) return null;
        try
        {
            // The release zip unpacks either flat or inside a "vgmstream-win64" folder.
            return Directory.EnumerateFiles(InstallDirectory, ExecutableName, SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool IsAvailable => FindExecutable() is not null;

    /// <summary>Downloads and unpacks the tool when it is missing.</summary>
    public static async Task<string> EnsureAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (FindExecutable() is { } existing) return existing;

        Directory.CreateDirectory(InstallDirectory);
        var archive = Path.Combine(Path.GetTempPath(), $"vgmstream-{Guid.NewGuid():N}.zip");
        progress?.Report("正在下载音频解码工具 vgmstream（约 4.6 MB）…");

        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LolDataExtractor/1.1");
                using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(archive);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("正在解压…");
            ZipFile.ExtractToDirectory(archive, InstallDirectory, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"音频解码工具下载失败：{ex.Message}。可手动下载 {DownloadUrl} 解压到 {InstallDirectory}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(archive)) File.Delete(archive);
            }
            catch (IOException)
            {
                // Leaving a temp file behind is harmless.
            }
        }

        return FindExecutable() ?? throw new InvalidOperationException(
            $"解压后没有找到 {ExecutableName}，请检查 {InstallDirectory}");
    }

    /// <summary>
    /// Converts one stream of <paramref name="inputPath"/> to a wav file.
    /// <paramref name="streamIndex"/> is the 1-based subsong of a <c>.bnk</c> (null for a bare wem).
    /// </summary>
    public static string ConvertToWav(string inputPath, int? streamIndex, string outputPath, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "-o", outputPath };
        if (streamIndex is { } index)
        {
            arguments.Add("-s");
            arguments.Add(index.ToString());
        }
        arguments.Add(inputPath);
        Run(arguments, outputPath, cancellationToken);
        return outputPath;
    }

    /// <summary>
    /// Decodes every subsong of a bank in one run. <paramref name="outputTemplate"/> is a path
    /// ending in <c>?s.wav</c>: the tool replaces <c>?s</c> with the 1-based subsong number.
    /// </summary>
    public static void ConvertSubsongs(string inputPath, string outputTemplate, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputTemplate));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        Run(
            new List<string> { "-i", "-S", "0", "-o", outputTemplate, inputPath },
            expectedPath: null,
            cancellationToken);
    }

    private static void Run(List<string> arguments, string? expectedPath, CancellationToken cancellationToken)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("尚未安装音频解码工具 vgmstream");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 vgmstream-cli");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("音频转码超时（60 秒）");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0 || (expectedPath is not null && !File.Exists(expectedPath)))
        {
            var message = error.GetAwaiter().GetResult().Trim();
            throw new InvalidOperationException(
                string.IsNullOrEmpty(message) ? $"vgmstream 转码失败（退出码 {process.ExitCode}）" : $"vgmstream 转码失败：{message}");
        }
    }
}
