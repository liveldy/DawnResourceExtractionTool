using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.Controls;

/// <summary>A searchable list of wad entries, used to pick a skeleton, clip or texture by hand.</summary>
public partial class EntryPickerWindow : Window
{
    private readonly List<WadEntryInfo> _entries;

    public EntryPickerWindow(IEnumerable<WadEntryInfo> entries, string title, string? extension)
    {
        InitializeComponent();

        Title = title;
        Header.Text = title;
        _entries = entries
            .Where(entry => entry.IsNamed)
            .Where(entry => extension is null || entry.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyFilter();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public WadEntryInfo? SelectedEntry { get; private set; }

    private void ApplyFilter()
    {
        var needle = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var filtered = needle.Length == 0
            ? _entries
            : _entries.Where(entry => entry.Path!.ToLowerInvariant().Contains(needle)).ToList();

        List.ItemsSource = filtered;
        Summary.Text = $"{filtered.Count:N0} / {_entries.Count:N0} 个条目";
        if (filtered.Count > 0) List.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void Accept_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        if (List.SelectedItem is not WadEntryInfo entry) return;
        SelectedEntry = entry;
        DialogResult = true;
    }
}
