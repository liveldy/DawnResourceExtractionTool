using System.Windows;
using Microsoft.Win32;

namespace LolDataExtractor.App.Services;

/// <summary>界面配色：黑夜（默认）/ 白天 / 跟随系统。</summary>
public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>
/// Swaps the two theme dictionaries — the HandyControl skin and our own palette — while the app runs.
/// Every brush in the XAML has to be referenced with <c>DynamicResource</c>; a <c>StaticResource</c>
/// keeps the colour it was loaded with and stays behind after a switch.
/// </summary>
public static class ThemeService
{
    private const string SkinDarkUri = "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml";
    private const string SkinLightUri = "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";

    private static readonly Uri PaletteDark = new("Themes/Palette.Dark.xaml", UriKind.Relative);
    private static readonly Uri PaletteLight = new("Themes/Palette.Light.xaml", UriKind.Relative);

    private static bool _watchingSystem;

    /// <summary>The mode last applied; the toolbar button cycles from here.</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Raised after a swap, so colours built in code can follow.</summary>
    public static event Action? Changed;

    public static AppTheme Parse(string? text)
        => Enum.TryParse<AppTheme>(text, ignoreCase: true, out var mode) ? mode : AppTheme.Dark;

    /// <summary>Caption of the theme button.</summary>
    public static string Label(AppTheme mode) => mode switch
    {
        AppTheme.Light => "白天",
        AppTheme.System => "跟随系统",
        _ => "黑夜",
    };

    /// <summary>The button's cycle: 黑夜 → 白天 → 跟随系统 → 黑夜.</summary>
    public static AppTheme Next(AppTheme mode) => mode switch
    {
        AppTheme.Dark => AppTheme.Light,
        AppTheme.Light => AppTheme.System,
        _ => AppTheme.Dark,
    };

    /// <summary>True when Windows itself is set to a light appearance.</summary>
    public static bool SystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The colours a mode really uses; 跟随系统 resolves to one of the other two.</summary>
    public static AppTheme Effective(AppTheme mode)
        => mode == AppTheme.System ? (SystemUsesLight() ? AppTheme.Light : AppTheme.Dark) : mode;

    /// <summary>Installs the dictionaries of a mode; safe to call before any window exists.</summary>
    public static void Apply(AppTheme mode)
    {
        Current = mode;
        var light = Effective(mode) == AppTheme.Light;

        if (Application.Current is { } app)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            Swap(dictionaries, IsSkin, new ResourceDictionary
            {
                Source = new Uri(light ? SkinLightUri : SkinDarkUri),
            });
            Swap(dictionaries, IsPalette, new ResourceDictionary { Source = light ? PaletteLight : PaletteDark });
        }

        WatchSystem(mode == AppTheme.System);
        Changed?.Invoke();
    }

    private static bool IsSkin(ResourceDictionary dictionary)
        => dictionary.Source?.OriginalString.Contains("/Themes/Skin", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPalette(ResourceDictionary dictionary)
        => dictionary.Source?.OriginalString.Contains("Themes/Palette.", StringComparison.OrdinalIgnoreCase) == true;

    private static void Swap(IList<ResourceDictionary> dictionaries,
        Func<ResourceDictionary, bool> match, ResourceDictionary replacement)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (!match(dictionaries[i])) continue;
            dictionaries[i] = replacement;
            return;
        }

        dictionaries.Add(replacement);
    }

    /// <summary>In 跟随系统 the windows appearance is watched, so a switch in Windows is followed at once.</summary>
    private static void WatchSystem(bool watch)
    {
        if (watch == _watchingSystem) return;
        _watchingSystem = watch;
        if (watch) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        else SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)) return;
        if (Application.Current is not { } app) return;

        app.Dispatcher.BeginInvoke(() =>
        {
            if (Current == AppTheme.System) Apply(AppTheme.System);
        });
    }
}
