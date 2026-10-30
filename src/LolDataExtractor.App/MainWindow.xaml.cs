using System.Windows;
using System.Windows.Controls;
using LolDataExtractor.App.ViewModels;

namespace LolDataExtractor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        Closed += (_, _) => ViewModel.Dispose();
    }

    public MainViewModel ViewModel { get; }

    public Task InitializeAsync(string? dataRoot) => ViewModel.InitializeAsync(dataRoot);

    /// <summary>Self test helper: switches to the named wad.</summary>
    public Task<bool> SelectWadAsync(string name) => ViewModel.SelectWadAsync(name);

    /// <summary>Self test helper: filters the tree and selects the first matching entry.</summary>
    public Task<bool> SelectEntryAsync(string pattern) => ViewModel.SelectFirstMatchAsync(pattern);

    /// <summary>Self test helper: switches the right panel to the export / log tab.</summary>
    public void ShowExportTab() => RightTabs.SelectedIndex = 1;

    /// <summary>Selecting a wad leaf in the wad tree browses it; folder rows are ignored.</summary>
    private void WadTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is WadTreeNode { Wad: { } wad }) ViewModel.SelectedWad = wad;
    }

    /// <summary>The tree view has no bindable SelectedItem, so the leaf selection is forwarded here.</summary>
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // A null selection only happens while the tree is rebuilt; keep the current preview.
        if (e.NewValue is TreeNodeVm node) ViewModel.SelectedNode = node;
    }

    /// <summary>Right click selects the item under the cursor so the context menu acts on it.</summary>
    private void TreeView_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is not null) item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
