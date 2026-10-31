using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LolDataExtractor.App.ViewModels;
using LolDataExtractor.Core;

namespace LolDataExtractor.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var root = GetArgument(e.Args, "--root");
        var screenshot = GetArgument(e.Args, "--screenshot");
        var exportOnce = GetArgument(e.Args, "--export-once");
        var exportPrefix = GetArgument(e.Args, "--export-prefix");
        var select = GetArgument(e.Args, "--select");
        var selectAll = HasFlag(e.Args, "--select-all");
        var wad = GetArgument(e.Args, "--wad");
        var audioSmoke = HasFlag(e.Args, "--audio-smoke");
        var playAnim = HasFlag(e.Args, "--play-anim");
        var pick = HasFlag(e.Args, "--pick");
        var tab = GetArgument(e.Args, "--tab");
        var scope = GetArgument(e.Args, "--export-scope");
        var preset = GetArgument(e.Args, "--export-preset");
        var transcribe = HasFlag(e.Args, "--transcribe");
        var exportStream = GetArgument(e.Args, "--export-stream");
        var wadFilter = GetArgument(e.Args, "--wad-filter");
        var checkAll = HasFlag(e.Args, "--check-all");

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        try
        {
            await window.InitializeAsync(root);
            if (wad is not null) await window.SelectWadAsync(wad);
            if (select is not null) await window.SelectEntryAsync(select);
            if (checkAll) window.ViewModel.CheckAllEntries();
            if (wadFilter is not null)
            {
                window.ViewModel.WadSearchText = wadFilter;
                window.ViewModel.Log($"自检：wad 搜索「{wadFilter}」→ {window.ViewModel.SelectionSummary}");
            }
            if (selectAll)
            {
                window.ViewModel.SelectAllCommand.Execute(null);
                window.ViewModel.Log("自检：全选后 " + window.ViewModel.SelectionSummary);
            }
            if (tab is not null) window.ShowTab(tab);
            if (playAnim) await RunAnimationSmokeTestAsync(window);
            if (audioSmoke) await RunAudioSmokeTestAsync(window);
            if (transcribe) await RunTranscribeSmokeTestAsync(window);
            if (exportStream is not null) await RunStreamExportSmokeTestAsync(window, exportStream);
            if (exportOnce is not null) await RunExportSmokeTestAsync(window, exportOnce, scope, preset);
            if (exportPrefix is not null) await RunPrefixExportAsync(window, exportPrefix);
            if (pick) await RunPickerSmokeTestAsync(window, screenshot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "英雄联盟数据提取工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (screenshot is not null)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(1500);
            SaveScreenshot(window, screenshot);
            SaveLog(window, screenshot);
            Shutdown();
        }
    }

    /// <summary>自检产物：日志存成截图旁边的 .log，脚本可以核对数字而不用读像素。</summary>
    private static void SaveLog(MainWindow window, string screenshotPath)
    {
        var path = Path.ChangeExtension(Path.GetFullPath(screenshotPath), ".log");
        File.WriteAllLines(path, window.ViewModel.Logs, new System.Text.UTF8Encoding(false));
    }

    /// <summary>自检入口：走一遍界面上的导出流程，用于无人值守验收。</summary>
    /// <param name="scope">勾选 / 当前 / 全部，省略时只导出当前 wad 的 Ahri 条目。</param>
    /// <param name="preset">texture / model / audio，省略时按 pattern + 扩展名过滤。</param>
    private static async Task RunExportSmokeTestAsync(MainWindow window, string outputDirectory,
        string? scope, string? preset)
    {
        var viewModel = window.ViewModel;
        viewModel.OutputRootText = outputDirectory;

        // Keep the wad the self test already opened (and its selection); only switch when needed,
        // and wait for the entry tree so a selection-based scope sees the node picked earlier.
        var current = viewModel.SelectedWad;
        if (current is null || !current.Name.StartsWith("Ahri", StringComparison.OrdinalIgnoreCase))
            await viewModel.SelectWadAsync("Ahri");
        if (viewModel.PendingEntries is { } loading) await loading;

        if (scope is not null)
        {
            viewModel.SelectedExportScope = scope switch
            {
                "current" => MainViewModel.ExportScopes[1],
                "all" => MainViewModel.ExportScopes[2],
                "selected" => MainViewModel.ExportScopes[3],
                _ => MainViewModel.ExportScopes[0],
            };
            viewModel.ExportPattern = "*";
            viewModel.ExportExtensions = string.Empty;
        }
        else
        {
            viewModel.ExportPattern = "*skins/base/*";
            viewModel.ExportExtensions = "skn,skl,tex";
        }

        if (preset is not null) viewModel.SelectCategoryPresetCommand.Execute(preset);

        viewModel.Log($"自检：界面导出 {outputDirectory}（范围 {viewModel.SelectedExportScope}，类别 {preset ?? "默认"}）");
        await viewModel.ExportAsync();
        viewModel.Log("自检：导出结束 - " + viewModel.StatusText);
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>自检入口：预览里的音频包只导出选中的那一条流，产物应是单个数 MB 以内的文件。</summary>
    private static async Task RunStreamExportSmokeTestAsync(MainWindow window, string outputDirectory)
    {
        var viewModel = window.ViewModel;
        viewModel.OutputRootText = outputDirectory;

        if (viewModel.AudioTracks.Count < 2)
        {
            viewModel.Log($"自检：当前条目只有 {viewModel.AudioTracks.Count} 条流，跳过单流导出");
            return;
        }

        var track = viewModel.AudioTracks[Math.Min(1, viewModel.AudioTracks.Count - 1)];
        viewModel.SelectedAudioTrack = track;
        viewModel.Log($"自检：导出选中的第 {track.Index + 1} 条流（{track.Title}）→ {outputDirectory}");

        viewModel.ExportCurrentCommand.Execute(null);
        for (var i = 0; i < 240 && !viewModel.StatusText.StartsWith("已导出", StringComparison.Ordinal)
                                   && !viewModel.StatusText.StartsWith("导出失败", StringComparison.Ordinal); i++)
        {
            await Task.Delay(250);
        }

        viewModel.Log("自检：单流导出结束 - " + viewModel.StatusText);
    }

    /// <summary>自检入口：打开手动选择对话框并截图，确认布局可用。</summary>
    private static async Task RunPickerSmokeTestAsync(MainWindow window, string? screenshot)
    {
        var picker = new Controls.EntryPickerWindow(window.ViewModel.CurrentEntries, "选择动画文件（.anm）", "anm")
        {
            Owner = window,
        };
        picker.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(800);

        if (screenshot is not null)
            SaveScreenshot(picker, Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(screenshot)) ?? ".",
                Path.GetFileNameWithoutExtension(screenshot) + "-picker" + Path.GetExtension(screenshot)));

        picker.Close();
    }

    /// <summary>自检入口：确认模型已自动关联骨骼/动画/贴图，并播放一段动画。</summary>
    private static async Task RunAnimationSmokeTestAsync(MainWindow window)
    {
        var viewModel = window.ViewModel;
        viewModel.Log($"自检：骨骼 {viewModel.SkeletonChoices.Count - 1} 个 · 动画 {viewModel.AnimationChoices.Count - 1} 个 · " +
                      $"贴图 {viewModel.TextureChoices.Count - 1} 个 · {viewModel.AnimationStatus}");

        viewModel.PlayAnimationCommand.Execute(null);
        await Task.Delay(900);
        viewModel.Log("自检：动画状态 = " + viewModel.AnimationStatus + " · 正在播放 = " + viewModel.IsAnimationPlaying);
    }

    /// <summary>自检入口：按目录前缀导出当前 wad 的一部分。</summary>
    private static async Task RunPrefixExportAsync(MainWindow window, string prefix)
    {
        var viewModel = window.ViewModel;
        viewModel.SelectedExportScope = MainViewModel.ExportScopes[1];
        viewModel.ExportPathPrefix = prefix;
        viewModel.ExportPattern = "*";
        viewModel.ExportExtensions = string.Empty;
        viewModel.Log("自检：目录前缀导出 " + prefix);
        await viewModel.ExportAsync();
        viewModel.Log("自检：目录导出完成 - " + viewModel.StatusText);
    }

    /// <summary>自检入口：确保音频工具可用，并试听当前条目（bnk 的第一条流或单条音频）。</summary>
    private static async Task RunAudioSmokeTestAsync(MainWindow window)
    {
        var viewModel = window.ViewModel;
        if (viewModel.AudioToolMissing)
        {
            viewModel.Log("自检：下载 vgmstream 音频解码工具");
            viewModel.DownloadAudioToolCommand.Execute(null);
            for (var i = 0; i < 120 && viewModel.IsBusy; i++) await Task.Delay(1000);
        }

        viewModel.Log($"自检：音频工具就绪 = {!viewModel.AudioToolMissing}");
        viewModel.PlayAudioCommand.Execute(null);
        await Task.Delay(1500);
        viewModel.Log("自检：音频状态 = " + viewModel.AudioStatus);
    }

    /// <summary>自检入口：把当前音频包里每条流都转成文字，并核对备注已落盘。</summary>
    private static async Task RunTranscribeSmokeTestAsync(MainWindow window)
    {
        var viewModel = window.ViewModel;
        viewModel.Log($"自检：模型存在 = {SpeechRecognizer.IsModelPresent} · 音频流 {viewModel.AudioTracks.Count} 条 · 备注文件 {viewModel.NotesPath}");

        viewModel.TranscribeAllCommand.Execute(null);
        for (var i = 0; i < 1200 && viewModel.IsTranscribing; i++) await Task.Delay(250);

        viewModel.Log("自检：转写状态 = " + viewModel.SpeechStatus);
        foreach (var track in viewModel.AudioTracks.Take(6))
            viewModel.Log($"自检：{track.Title} → {(track.HasNote ? track.Note : "（无文字）")}");

        var reloaded = AudioNoteStore.Load();
        viewModel.Log($"自检：备注 {viewModel.SavedNoteCount} 条，重新读盘 {reloaded.Count} 条，文件存在 = {File.Exists(viewModel.NotesPath)}");
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(name.Length + 1)..].Trim('"');
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim('"');
        }
        return null;
    }

    /// <summary>Renders the window content to a PNG; used by the UI self test.</summary>
    private static void SaveScreenshot(Window window, string path)
    {
        var element = window.Content as FrameworkElement ?? window;

        // 截图时去掉外层留白，让像素尺寸等于内容尺寸
        var margin = element.Margin;
        element.Margin = new Thickness(0);
        window.UpdateLayout();

        var width = (int)Math.Ceiling(element.ActualWidth > 0 ? element.ActualWidth : window.Width);
        var height = (int)Math.Ceiling(element.ActualHeight > 0 ? element.ActualHeight : window.Height);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("窗口尚未完成布局，无法截图");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        element.Margin = margin;
        window.UpdateLayout();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
