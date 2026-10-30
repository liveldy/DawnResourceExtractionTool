using System.Diagnostics;
using System.Net.Http;
using NAudio.Wave;
using Whisper.net;

namespace LolDataExtractor.Core;

/// <summary>
/// Speech to text for the voice lines, on demand: the ggml model is downloaded once into the tool
/// directory, then whisper runs locally, so no audio ever leaves the machine. Whisper wants 16 kHz
/// mono floats, which <see cref="Transcriber"/> produces from whatever wav vgmstream decoded.
/// </summary>
public static class SpeechRecognizer
{
    public const string ModelFileName = "ggml-base.bin";

    /// <summary>Chinese (simplified) context bias: whisper otherwise mixes in traditional forms.</summary>
    public const string ChinesePrompt = "以下是普通话简体中文的游戏语音；它属于英雄联盟的台词。";

    /// <summary>Model mirror; huggingface.co itself is not reachable from every network.</summary>
    public const string ModelUrl = "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    public const long ModelBytes = 147_951_465;

    /// <summary>Where the model is kept, next to vgmstream below the tool data directory.</summary>
    public static string InstallDirectory => Path.Combine(AppSettings.DefaultDataDirectory(), "tools", "whisper");

    public static string ModelPath => Path.Combine(InstallDirectory, ModelFileName);

    public static bool IsModelPresent => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 1_000_000;

    /// <summary>
    /// Whisper language for a wad such as <c>Ahri.zh_CN.wad.client</c>; a wad without a locale holds
    /// the default (English) audio, so whisper is left to detect the language.
    /// </summary>
    public static string LanguageOf(string wadFileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            wadFileName, @"\.([A-Za-z]{2,3})(?:_[A-Za-z]{2,4})?\.wad", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return "auto";

        var language = match.Groups[1].Value.ToLowerInvariant();
        return language switch
        {
            "zh" => "zh",
            "en" => "en",
            "ja" => "ja",
            "ko" => "ko",
            "es" => "es",
            "fr" => "fr",
            "de" => "de",
            "ru" => "ru",
            "pt" => "pt",
            "tr" => "tr",
            "pl" => "pl",
            "th" => "th",
            "vi" => "vi",
            "id" => "id",
            _ => "auto",
        };
    }

    /// <summary>Downloads the model when it is missing; safe to call repeatedly.</summary>
    public static async Task<string> EnsureModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsModelPresent) return ModelPath;

        Directory.CreateDirectory(InstallDirectory);
        var temp = ModelPath + ".part";
        progress?.Report($"正在下载语音识别模型（约 {ExtractionSummary.Format(ModelBytes)}，只需一次）…");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LolDataExtractor/1.3");
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? ModelBytes;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress?.Report($"正在下载语音识别模型 {done * 100 / Math.Max(1, total)}%（{ExtractionSummary.Format(done)} / {ExtractionSummary.Format(total)}）");
                }
            }

            File.Move(temp, ModelPath, overwrite: true);
            progress?.Report("语音识别模型已就绪");
            return ModelPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"语音识别模型下载失败：{ex.Message}。可手动下载 {ModelUrl} 放到 {ModelPath}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (IOException)
            {
                // Ignored: a leftover part file is harmless.
            }
        }
    }
}

/// <summary>
/// One whisper context reused for a run of transcriptions; the processor is not thread safe, so a
/// transcriber is meant for one worker at a time.
/// </summary>
public sealed class Transcriber : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private Transcriber(WhisperFactory factory, WhisperProcessor processor, string language)
    {
        _factory = factory;
        _processor = processor;
        Language = language;
    }

    public string Language { get; }

    /// <summary>Loads the model; the first call costs a few hundred milliseconds.</summary>
    public static Transcriber Create(string language = "zh")
    {
        if (!SpeechRecognizer.IsModelPresent)
            throw new InvalidOperationException("尚未下载语音识别模型");

        var factory = WhisperFactory.FromPath(SpeechRecognizer.ModelPath);
        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));

        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            builder = builder.WithPrompt(SpeechRecognizer.ChinesePrompt);

        return new Transcriber(factory, builder.Build(), language);
    }

    /// <summary>Transcribes one wav file (any rate, mono or stereo) and returns the plain text.</summary>
    public async Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken = default)
    {
        var samples = await Task.Run(() => ReadMono16k(wavPath), cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new System.Text.StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                text.Append(segment.Text);

            return Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Trims whisper's spacing and the bracketed sound markers it likes to emit.</summary>
    private static string Clean(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return string.Empty;

        // "(Ha!)" / "[BLANK_AUDIO]" style annotations carry no speech.
        if (trimmed.Length <= 24 && trimmed.StartsWith('(') && trimmed.EndsWith(')')) return string.Empty;
        if (trimmed.Length <= 24 && trimmed.StartsWith('[') && trimmed.EndsWith(']')) return string.Empty;

        return trimmed;
    }

    /// <summary>Decodes a wav into 16 kHz mono floats with plain linear resampling.</summary>
    public static float[] ReadMono16k(string wavPath)
    {
        using var reader = new WaveFileReader(wavPath);
        var format = reader.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var mono = new List<float>((int)Math.Min(int.MaxValue, reader.Length / Math.Max(1, format.BlockAlign)));

        var buffer = new byte[format.BlockAlign * 4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var frames = read / format.BlockAlign;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                    sum += Sample(buffer, frame * format.BlockAlign + channel * bytesPerSample, format);
                mono.Add(sum / channels);
            }
        }

        if (mono.Count == 0) return Array.Empty<float>();
        if (format.SampleRate == 16000) return mono.ToArray();

        var ratio = (double)format.SampleRate / 16000;
        var count = (int)(mono.Count / ratio);
        var result = new float[Math.Max(1, count)];
        for (var i = 0; i < result.Length; i++)
        {
            var position = i * ratio;
            var index = (int)position;
            var fraction = position - index;
            var a = mono[Math.Min(index, mono.Count - 1)];
            var b = mono[Math.Min(index + 1, mono.Count - 1)];
            result[i] = (float)(a + (b - a) * fraction);
        }
        return result;
    }

    private static float Sample(byte[] buffer, int offset, WaveFormat format) => format.BitsPerSample switch
    {
        8 => (buffer[offset] - 128) / 128f,
        16 => BitConverter.ToInt16(buffer, offset) / 32768f,
        24 => (buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16)) / 8388608f,
        32 when format.Encoding == WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(buffer, offset),
        32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
        _ => 0f,
    };

    /// <summary>Runs a quick transcription and reports how long it took, for the self test.</summary>
    public static async Task<TimeSpan> TranscribeTimedAsync(string wavPath, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        using var transcriber = Create();
        await transcriber.TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);
        return clock.Elapsed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processor.Dispose();
        _factory.Dispose();
        _gate.Dispose();
    }
}
