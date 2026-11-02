using System.Windows.Media;
using DawnResourceExtractionTool.Core;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App.ViewModels;

/// <summary>
/// The square button right of the theme button: it flips the whole window between 中文 and English.
/// The choice lands in settings.json, so the next start opens in the same language.
/// </summary>
public sealed partial class MainViewModel
{
    // 24×24 方框里的地球：外圈 + 赤道 + 两条经线
    private const string GlobePath =
        "M12,2.5 A9.5,9.5 0 1,1 11.99,2.5 Z M2.5,12 H21.5 " +
        "M12,2.5 C7.6,7 7.6,17 12,21.5 M12,2.5 C16.4,7 16.4,17 12,21.5";

    private static readonly Geometry GlobeIcon = Geometry.Parse(GlobePath);

    private AppLanguage _language = AppLanguage.Chinese;

    /// <summary>Name of the language in use, written in that language: 中文 / English.</summary>
    public string LanguageLabel => AppLanguages.NativeName(_language);

    /// <summary>Icon on the button: a globe, the same size as the theme icon.</summary>
    public Geometry LanguageIcon => GlobeIcon;

    /// <summary>Header line under the title: 版本 1.1.3 · 2026-11-01 发布 / Version 1.1.3 · released 2026-11-01.</summary>
    public string AppSummary => AppInfo.Summary;

    /// <summary>"界面语言：中文（点一下切到 English）" for the tool tip.</summary>
    public string LanguageHint => Strings.T("Toolbar.Language.Hint", LanguageLabel,
        AppLanguages.NativeName(AppLanguages.Other(_language)));

    public RelayCommand CycleLanguageCommand { get; private set; } = null!;

    /// <summary>Reads the remembered language (or the system one) and installs it; called from the constructor.</summary>
    private void InitializeLanguage()
    {
        _language = AppLanguages.Parse(_settings.Language ?? AppLanguages.Tag(AppLanguages.SystemDefault()));
        Strings.Language = _language;
        CycleLanguageCommand = new RelayCommand(() => SetLanguage(AppLanguages.Other(_language)));
        Strings.Changed += OnLanguageChanged;
    }

    /// <summary>Applies a language, remembers it and says so in the log; the self test drives this too.</summary>
    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            Strings.Language = language;
            return;
        }

        _language = language;
        _settings.Language = AppLanguages.Tag(language);
        _settings.Save();
        Strings.Language = language;   // raises Strings.Changed, which runs OnLanguageChanged
        Log(Strings.T("Toolbar.Language.Log", LanguageLabel));
    }

    /// <summary>Every text on screen is rebuilt from the new language.</summary>
    private void OnLanguageChanged()
    {
        LocalizeOptions();
        OnPropertyChanged(nameof(AppSummary));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageHint));
        OnPropertyChanged(nameof(LanguageIcon));

        // Placeholders are computed when they are shown, not stored: while nothing is selected or scanned
        // there is no later event to refresh them, so recompute them here.
        if (_selectedNode is null && ShowMessage) PreviewMessage = Strings.T("App.Preview.Hint");
        if (Wads.Count == 0)
        {
            HashStatus = Strings.T("App.Status.HashNotBuilt");
            StatusText = Strings.T("App.Status.Ready");
            UpdateSelectionSummary();
        }

        // Empty name = "everything changed": the rest of the window re-reads its bindings.
        OnPropertyChanged(string.Empty);
    }
}
