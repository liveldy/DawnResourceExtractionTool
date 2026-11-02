using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App.Localization;

/// <summary>
/// <c>{loc:Loc Ui.Main.Title}</c> puts one localized text into XAML. The value is a one way binding to
/// <see cref="LocalizedText"/>, so pressing the language button redraws every text in place.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>Localization key, e.g. <c>Ui.Main.Title</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = LocalizedText.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}

/// <summary>
/// Source of every <see cref="LocExtension"/> binding: an indexer over <see cref="Strings"/>. It raises
/// one notification when the language changes, which is what makes all bound texts redraw.
/// </summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private LocalizedText() => Strings.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public static LocalizedText Instance { get; } = new();

    public string this[string key] => Strings.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
