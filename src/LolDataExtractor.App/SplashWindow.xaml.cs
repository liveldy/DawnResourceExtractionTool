using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LolDataExtractor.App.ViewModels;

namespace LolDataExtractor.App;

/// <summary>
/// 开局页：程序目录里的 <c>load.mp4</c> 铺满整屏循环播放，上面叠标题、版本与当前加载状态。
/// 资源没准备好之前点不动；准备好了（<see cref="MarkReady"/>）就一直停在这里，等用户点一下再进主窗口。
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>这段时间内的点击当作误触，不解锁。</summary>
    private const int SkipGuardMilliseconds = 700;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>用户点了「进入」或窗口被关掉时完成，主窗口在这之后才显示。</summary>
    private readonly TaskCompletionSource<bool> _skipped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SplashWindow(string videoPath)
    {
        InitializeComponent();
        VideoUri = new Uri(videoPath);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private Uri VideoUri { get; }

    /// <summary>程序目录里的开局视频，没有就返回 null（界面直接打开）。</summary>
    public static string? VideoPath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "load.mp4");
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>True once the app finished its startup work; only then can the page be skipped.</summary>
    public bool IsReady { get; private set; }

    /// <summary>让加载状态显示主窗口正在做的事（读索引、扫描 wad、下载哈希表…）。</summary>
    public void Attach(MainViewModel viewModel) => DataContext = viewModel;

    /// <summary>自检用：视频解码出来的宽高，0 表示没解出来。</summary>
    public int VideoWidth => Backdrop.NaturalVideoWidth;

    public int VideoHeight => Backdrop.NaturalVideoHeight;

    /// <summary>自检用：当前播放位置。</summary>
    public string PositionText => Backdrop.Position.ToString(@"hh\:mm\:ss");

    /// <summary>
    /// Resources are ready: the page stays until the user clicks (or presses Esc / Enter / Space).
    /// Before this, clicks are ignored — the loading screen must not vanish mid download.
    /// </summary>
    public void MarkReady()
    {
        IsReady = true;
        SkipHint.Visibility = Visibility.Visible;
    }

    /// <summary>Waits for the click that enters the main window (or for the window to be closed).</summary>
    public Task WaitForSkipAsync() => _skipped.Task;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Centre.BeginAnimation(OpacityProperty, fade);

        try
        {
            Backdrop.Source = VideoUri;
            Backdrop.Play();
        }
        catch (Exception)
        {
            // A broken or unsupported video must never keep the app from starting.
            Backdrop.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _skipped.TrySetResult(true);
        Backdrop.Stop();
        Backdrop.Source = null;
        Backdrop.Close();
    }

    private void Backdrop_MediaEnded(object sender, RoutedEventArgs e)
    {
        Backdrop.Position = TimeSpan.Zero;
        Backdrop.Play();
    }

    private void Backdrop_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        => Backdrop.Visibility = Visibility.Collapsed;

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        Skip();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.Escape or Key.Enter or Key.Space) Skip();
    }

    private void Skip()
    {
        if (!IsReady || _clock.ElapsedMilliseconds < SkipGuardMilliseconds || !IsVisible) return;
        Close();
    }
}
