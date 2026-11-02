using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using DawnResourceExtractionTool.App.ViewModels;

namespace DawnResourceExtractionTool.App;

/// <summary>
/// 开局页：一个居中的无边框小窗（面积约占屏幕五分之一，像 Adobe / MATLAB 那样），
/// 程序目录里的 <c>load.mp4</c> 在窗口里循环播放，上面叠标题、版本与当前加载状态。
/// 启动工作做完就由 <see cref="App"/> 直接关掉它进主窗口，不需要点。
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>窗口边长占屏幕边长的比例：0.447² ≈ 1/5 的面积。</summary>
    private const double AreaScale = 0.447;

    public SplashWindow(string videoPath)
    {
        InitializeComponent();

        // 居中、约占屏幕面积五分之一；尺寸按当前屏幕工作区算，高分屏不会跟着放大到满屏。
        var work = SystemParameters.WorkArea;
        Width = Math.Round(work.Width * AreaScale);
        Height = Math.Round(work.Height * AreaScale);

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

    /// <summary>让加载状态显示主窗口正在做的事（读索引、扫描 wad、下载哈希表…）。</summary>
    public void Attach(MainViewModel viewModel) => DataContext = viewModel;

    /// <summary>自检用：视频解码出来的宽高，0 表示没解出来。</summary>
    public int VideoWidth => Backdrop.NaturalVideoWidth;

    public int VideoHeight => Backdrop.NaturalVideoHeight;

    /// <summary>自检用：当前播放位置。</summary>
    public string PositionText => Backdrop.Position.ToString(@"hh\:mm\:ss");

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
}
