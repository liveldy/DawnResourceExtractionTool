using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App.ViewModels;

/// <summary>
/// The "关于" card: what the tool does with the game files, and in whose hands the assets stay.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Shown in the card; the wording is a promise the tool has to keep.</summary>
    public string AboutNotice => Strings.T("About.Notice");

    private bool _isAboutOpen;

    /// <summary>True while the about card covers the window.</summary>
    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        private set => SetProperty(ref _isAboutOpen, value);
    }

    public string AboutText => AboutNotice;

    public RelayCommand ShowAboutCommand { get; private set; } = null!;

    public RelayCommand CloseAboutCommand { get; private set; } = null!;

    private void InitializeAbout()
    {
        ShowAboutCommand = new RelayCommand(() => IsAboutOpen = true);
        CloseAboutCommand = new RelayCommand(() => IsAboutOpen = false);
    }
}
