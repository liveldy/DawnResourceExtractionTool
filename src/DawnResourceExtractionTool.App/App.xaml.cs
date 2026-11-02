using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DawnResourceExtractionTool.App.ViewModels;
using DawnResourceExtractionTool.Core;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App;

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
        var catalog = GetArgument(e.Args, "--catalog");
        var catalogSelect = GetArgument(e.Args, "--catalog-select");
        var catalogExport = GetArgument(e.Args, "--catalog-export");
        var catalogExportScope = GetArgument(e.Args, "--catalog-export-scope");
        var jsonJump = HasFlag(e.Args, "--json-jump");
        var catalogSearch = GetArgument(e.Args, "--catalog-search");
        var about = HasFlag(e.Args, "--about");
        var splashPreview = HasFlag(e.Args, "--splash");
        var hashSmoke = HasFlag(e.Args, "--hash-smoke");
        var resourceCheck = HasFlag(e.Args, "--resources");
        var theme = GetArgument(e.Args, "--theme");
        var lang = GetArgument(e.Args, "--lang");

        var window = new MainWindow();
        MainWindow = window;

        // 主题：主窗口构造时已按 settings.json 装好，这里只给自检换一次并记进日志。
        if (theme is not null)
        {
            window.ViewModel.SetTheme(Services.ThemeService.Parse(theme));
            window.ViewModel.Log(Strings.T("Win.SelfTest.Theme", window.ViewModel.ThemeDetail));
        }

        // 语言：--lang zh|en 切换界面语言，也会写进 settings.json（和其它参数一样算自检）。
        // 逗号分隔表示依次切过去（--lang en,zh），用来核对来回切换；第一个在这里装好，免得
        // 扫描时的状态文字还是旧语言，其余的等界面排好版再切（见下面 SwitchLanguageAsync）。
        var languages = lang?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        ?? Array.Empty<string>();
        if (languages.Length > 0)
        {
            window.ViewModel.SetLanguage(Core.Localization.AppLanguages.Parse(languages[0]));
            window.ViewModel.Log(Strings.T("Win.SelfTest.Language", window.ViewModel.LanguageLabel));
        }

        // 开局页：程序目录里有 load.mp4 就全屏播一遍，主窗口在它后面准备。带参数启动的都是自检，
        // 不显示开局页，截图流程才不会被它挡住。
        var selfTest = e.Args.Length > 0;
        SplashWindow? splash = null;
        if ((!selfTest || splashPreview) && SplashWindow.VideoPath is { } videoPath)
        {
            splash = new SplashWindow(videoPath);
            splash.Attach(window.ViewModel);
            splash.Show();
        }
        else
        {
            window.Show();
        }

        try
        {
            await window.InitializeAsync(root);

            // 资源检查：开局页还在时把要下载的东西补齐（哈希表、音频解码工具、语音模型…），
            // 清单在 ViewModels/MainViewModel.Resources.cs 的 BuildResourceList，加新资源只加一行。自检启动不触发。
            if (resourceCheck)
            {
                window.ViewModel.Log(Strings.T("Win.SelfTest.Resources", window.ViewModel.ResourceSummary,
                    window.ViewModel.AllResourcesReady));
            }
            else if (hashSmoke)
            {
                var needed = window.ViewModel.NeedsHashTable;
                var ready = await window.ViewModel.EnsureHashTableAsync();
                window.ViewModel.Log(Strings.T("Win.SelfTest.HashTable", needed, ready, window.ViewModel.HashStatus));
            }
            else if (!selfTest)
            {
                await window.ViewModel.EnsureAllResourcesAsync();
            }
            if (wad is not null) await window.SelectWadAsync(wad);
            if (select is not null) await window.SelectEntryAsync(select);
            if (checkAll) window.ViewModel.CheckAllEntries();
            if (wadFilter is not null)
            {
                window.ViewModel.WadSearchText = wadFilter;
                window.ViewModel.Log(Strings.T("Win.SelfTest.WadSearch", wadFilter,
                    window.ViewModel.SelectionSummary));
            }
            if (selectAll)
            {
                window.ViewModel.SelectAllCommand.Execute(null);
                window.ViewModel.Log(Strings.T("Win.SelfTest.SelectAll", window.ViewModel.SelectionSummary));
            }
            if (tab is not null) window.ShowTab(tab);
            if (catalog is not null || catalogSelect is not null || catalogExport is not null || jsonJump
                || catalogSearch is not null)
            {
                var opened = await window.ShowCatalogAsync(catalogSelect ?? catalog, catalogExportScope);
                window.ViewModel.Log(Strings.T("Win.SelfTest.CatalogBrowse",
                    opened ? Strings.T("Win.SelfTest.Opened") : Strings.T("Win.SelfTest.NoCatalogNodes"),
                    window.ViewModel.CatalogStatus));
            }
            if (catalogExport is not null)
            {
                window.ViewModel.OutputRootText = catalogExport;
                await window.ViewModel.ExportSelectedCatalogAsync();
            }
            if (catalogSearch is not null)
            {
                window.ViewModel.CatalogSearchText = catalogSearch;
                await Task.Delay(700);   // the search box debounces 300 ms
                var hit = window.ViewModel.CatalogSearchRoots.FirstOrDefault();
                window.ViewModel.Log(Strings.T("Win.SelfTest.CatalogSearch", catalogSearch,
                                       hit is null ? Strings.T("Win.SelfTest.NoResults") : hit.CountText) +
                                   Strings.T("Win.SelfTest.CatalogSearchDetail",
                                       window.ViewModel.CatalogShowingSearch, window.ViewModel.CatalogTreeVisible));
            }
            if (about) window.ViewModel.ShowAboutCommand.Execute(null);
            if (jsonJump) await RunJsonJumpAsync(window);
            if (playAnim) await RunAnimationSmokeTestAsync(window);
            if (audioSmoke) await RunAudioSmokeTestAsync(window);
            if (transcribe) await RunTranscribeSmokeTestAsync(window);
            if (exportStream is not null) await RunStreamExportSmokeTestAsync(window, exportStream);
            if (exportOnce is not null) await RunExportSmokeTestAsync(window, exportOnce, scope, preset);
            if (exportPrefix is not null) await RunPrefixExportAsync(window, exportPrefix);
            if (pick) await RunPickerSmokeTestAsync(window, screenshot);
            if (languages.Length > 1) await SwitchLanguageAsync(window, languages.Skip(1));
        }
        catch (Exception ex)
        {
            // 开局页盖在最上层，先关掉它，错误提示才看得见。
            if (splash is not null && splash.IsVisible) splash.Close();
            MessageBox.Show(ex.Message, Strings.T("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // 启动工作做完就直接进主窗口，不用再点一下。
        if (splash is not null && !splashPreview)
        {
            splash.Close();
            window.Show();
            window.Activate();
        }

        if (screenshot is not null)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(1500);

            // --splash 时截图拍的是开局页，并顺手把解码结果记进日志。
            var target = splashPreview && splash is not null ? (Window)splash : window;
            if (splashPreview && splash is not null)
                window.ViewModel.Log(Strings.T("Win.SelfTest.Splash", splash.VideoWidth, splash.VideoHeight,
                    splash.PositionText));
            SaveScreenshot(target, screenshot);
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

    /// <summary>自检入口：点一下预览里 JSON 的第一个 /lol-game-data/ 链接，核对能跳到资源。</summary>
    private static async Task RunJsonJumpAsync(MainWindow window)
    {
        var json = window.ViewModel.PreviewJson;
        if (string.IsNullOrEmpty(json))
        {
            window.ViewModel.Log(Strings.T("Win.SelfTest.JsonEmpty"));
            return;
        }

        var match = System.Text.RegularExpressions.Regex.Match(json, @"/lol-game-data/[^""\\]+");
        if (!match.Success)
        {
            window.ViewModel.Log(Strings.T("Win.SelfTest.JsonNoPath"));
            return;
        }

        var opened = await window.ViewModel.OpenClientAssetAsync(match.Value);
        window.ViewModel.Log(Strings.T("Win.SelfTest.JsonLink", match.Value,
            opened ? Strings.T("Win.SelfTest.Opened") : Strings.T("Win.SelfTest.NotFound"),
            window.ViewModel.StatusText));
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
            viewModel.ExportScopeIndex = scope switch
            {
                "current" => 1,
                "all" => 2,
                "selected" => 3,
                _ => 0,
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

        viewModel.Log(Strings.T("Win.SelfTest.UiExport", outputDirectory, viewModel.SelectedExportScope,
            preset ?? Strings.T("Win.SelfTest.Default")));
        await viewModel.ExportAsync();
        viewModel.Log(Strings.T("Win.SelfTest.ExportFinished", viewModel.StatusText));
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
            viewModel.Log(Strings.T("Win.SelfTest.StreamSkip", viewModel.AudioTracks.Count));
            return;
        }

        var track = viewModel.AudioTracks[Math.Min(1, viewModel.AudioTracks.Count - 1)];
        viewModel.SelectedAudioTrack = track;
        viewModel.Log(Strings.T("Win.SelfTest.StreamExport", track.Index + 1, track.Title, outputDirectory));

        viewModel.ExportCurrentCommand.Execute(null);
        for (var i = 0; i < 240 && !viewModel.CurrentExportFinished; i++)
        {
            await Task.Delay(250);
        }

        viewModel.Log(Strings.T("Win.SelfTest.StreamExportFinished", viewModel.StatusText));
    }

    /// <summary>自检入口：打开手动选择对话框并截图，确认布局可用。</summary>
    private static async Task RunPickerSmokeTestAsync(MainWindow window, string? screenshot)
    {
        var picker = new Controls.EntryPickerWindow(window.ViewModel.CurrentEntries,
            Strings.T("Win.Dialog.ChooseAnimation"), "anm")
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
        viewModel.Log(Strings.T("Win.SelfTest.Animation", viewModel.SkeletonChoices.Count - 1,
            viewModel.AnimationChoices.Count - 1, viewModel.TextureChoices.Count - 1, viewModel.AnimationStatus));

        viewModel.PlayAnimationCommand.Execute(null);
        await Task.Delay(900);
        viewModel.Log(Strings.T("Win.SelfTest.AnimationState", viewModel.AnimationStatus, viewModel.IsAnimationPlaying));
    }

    /// <summary>自检入口：按目录前缀导出当前 wad 的一部分。</summary>
    private static async Task RunPrefixExportAsync(MainWindow window, string prefix)
    {
        var viewModel = window.ViewModel;
        viewModel.ExportScopeIndex = 1;
        viewModel.ExportPathPrefix = prefix;
        viewModel.ExportPattern = "*";
        viewModel.ExportExtensions = string.Empty;
        viewModel.Log(Strings.T("Win.SelfTest.PrefixExport", prefix));
        await viewModel.ExportAsync();
        viewModel.Log(Strings.T("Win.SelfTest.PrefixExportFinished", viewModel.StatusText));
    }

    /// <summary>自检入口：确保音频工具可用，并试听当前条目（bnk 的第一条流或单条音频）。</summary>
    private static async Task RunAudioSmokeTestAsync(MainWindow window)
    {
        var viewModel = window.ViewModel;
        if (viewModel.AudioToolMissing)
        {
            viewModel.Log(Strings.T("Win.SelfTest.AudioToolDownload"));
            viewModel.DownloadAudioToolCommand.Execute(null);
            for (var i = 0; i < 120 && viewModel.IsBusy; i++) await Task.Delay(1000);
        }

        viewModel.Log(Strings.T("Win.SelfTest.AudioToolReady", !viewModel.AudioToolMissing));
        viewModel.PlayAudioCommand.Execute(null);
        await Task.Delay(1500);
        viewModel.Log(Strings.T("Win.SelfTest.AudioState", viewModel.AudioStatus));
    }

    /// <summary>自检入口：把当前音频包里每条流都转成文字，并核对备注已落盘。</summary>
    private static async Task RunTranscribeSmokeTestAsync(MainWindow window)
    {
        var viewModel = window.ViewModel;
        viewModel.Log(Strings.T("Win.SelfTest.TranscribeSetup", SpeechRecognizer.IsModelPresent,
            viewModel.AudioTracks.Count, viewModel.NotesPath));

        viewModel.TranscribeAllCommand.Execute(null);
        for (var i = 0; i < 1200 && viewModel.IsTranscribing; i++) await Task.Delay(250);

        viewModel.Log(Strings.T("Win.SelfTest.TranscribeState", viewModel.SpeechStatus));
        foreach (var track in viewModel.AudioTracks.Take(6))
            viewModel.Log(Strings.T("Win.SelfTest.TranscribeTrack", track.Title,
                track.HasNote ? track.Note : Strings.T("Win.SelfTest.NoText")));

        var reloaded = AudioNoteStore.Load();
        viewModel.Log(Strings.T("Win.SelfTest.Notes", viewModel.SavedNoteCount, reloaded.Count,
            File.Exists(viewModel.NotesPath)));
    }

    /// <summary>
    /// 自检入口：界面已经排好版之后再把剩下的语言依次切过去，走的正是用户点语言的按钮那条路
    /// （选项框已经建好，切语言时它们会重新挂上新的列表）。
    /// </summary>
    private static async Task SwitchLanguageAsync(MainWindow window, IEnumerable<string> tags)
    {
        var viewModel = window.ViewModel;
        foreach (var tag in tags)
        {
            viewModel.SetLanguage(Core.Localization.AppLanguages.Parse(tag));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            viewModel.Log(Strings.T("Win.SelfTest.Language", viewModel.LanguageLabel));

            // 切换只该换文字：这些选择必须原样留着（曾经在第二次切换时崩在这里）。
            viewModel.Log(Strings.T("Win.SelfTest.LanguageState",
                viewModel.SelectedCategory?.Display ?? "-",
                viewModel.ExportScopeIndex,
                viewModel.SelectedAudioFormat.Value,
                CatalogLanguages.CurrentFolder));
        }
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
        if (width <= 0 || height <= 0) throw new InvalidOperationException(Strings.T("Win.Error.WindowNotLaidOut"));

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
