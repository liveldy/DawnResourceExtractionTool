using System.Windows.Media;
using LolDataExtractor.App.Services;

namespace LolDataExtractor.App.ViewModels;

/// <summary>
/// The one square toolbar button that switches the whole window between 黑夜 / 白天 / 跟随系统. The
/// choice is kept in settings.json, so the next start opens in the same colours.
/// </summary>
public sealed partial class MainViewModel
{
    // 24×24 方框里的描边图标：月亮、太阳、显示器（跟随系统）
    private const string MoonPath = "M20.5,14.7 A9,9 0 1,1 9.3,1.4 A7.2,7.2 0 0,0 20.5,14.7 Z";

    private const string SunPath =
        "M12,7.5 A4.5,4.5 0 1,1 11.99,7.5 Z M12,1 V4 M12,20 V23 M1,12 H4 M20,12 H23 " +
        "M4.2,4.2 L6.3,6.3 M17.7,17.7 L19.8,19.8 M4.2,19.8 L6.3,17.7 M17.7,6.3 L19.8,4.2";

    private const string SystemPath = "M3,4.5 H21 V16 H3 Z M9,20.5 H15 M12,16 V20.5";

    private static readonly Geometry MoonIcon = Geometry.Parse(MoonPath);
    private static readonly Geometry SunIcon = Geometry.Parse(SunPath);
    private static readonly Geometry SystemIcon = Geometry.Parse(SystemPath);

    private AppTheme _themeMode = AppTheme.Dark;

    /// <summary>Caption of the mode, used in the log and the tool tip.</summary>
    public string ThemeLabel => ThemeService.Label(_themeMode);

    /// <summary>Icon on the button: 月亮 for 黑夜, 太阳 for 白天, 显示器 for 跟随系统.</summary>
    public Geometry ThemeIcon => _themeMode switch
    {
        AppTheme.Light => SunIcon,
        AppTheme.System => SystemIcon,
        _ => MoonIcon,
    };

    /// <summary>"界面主题：黑夜（点一下切到白天）" for the tool tip.</summary>
    public string ThemeHint => $"界面主题：{ThemeDetail}（点一下切到{ThemeService.Label(ThemeService.Next(_themeMode))}）";

    /// <summary>"黑夜" / "跟随系统（当前系统是白天）"; what is in effect is spelled out for 跟随系统.</summary>
    public string ThemeDetail => _themeMode == AppTheme.System
        ? $"跟随系统（当前系统是{ThemeService.Label(ThemeService.Effective(_themeMode))}）"
        : ThemeService.Label(_themeMode);

    public RelayCommand CycleThemeCommand { get; private set; } = null!;

    /// <summary>Reads the remembered mode and installs it; called once from the constructor.</summary>
    private void InitializeTheme()
    {
        _themeMode = ThemeService.Parse(_settings.Theme);
        CycleThemeCommand = new RelayCommand(() => SetTheme(ThemeService.Next(_themeMode)));
        ThemeService.Changed += () => OnPropertyChanged(nameof(ThemeHint));
        ThemeService.Apply(_themeMode);
    }

    /// <summary>Applies a mode, remembers it and says so in the log; the self test drives this too.</summary>
    public void SetTheme(AppTheme mode)
    {
        _themeMode = mode;
        _settings.Theme = mode.ToString();
        _settings.Save();
        ThemeService.Apply(mode);

        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(ThemeDetail));
        OnPropertyChanged(nameof(ThemeHint));
        Log($"界面主题：{ThemeDetail}");
    }
}
