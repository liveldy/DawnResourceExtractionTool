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

    /// <summary>Typing a folder and pressing Enter scans it, like the「扫描」button does.</summary>
    private void DataRootBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        if (ViewModel.ScanCommand.CanExecute(null)) ViewModel.ScanCommand.Execute(null);
    }

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

    /// <summary>Drag limits of the three columns, in device independent pixels.</summary>
    private const double WadColumnMin = 200;
    private const double TreeColumnMin = 320;
    private const double PreviewColumnMin = 360;
    private const double GutterWidth = 10;

    /// <summary>Space the two gutters take out of the grid.</summary>
    private static double Gutters => GutterWidth * 2;

    /// <summary>
    /// Left splitter. The right column keeps its width, so the left one can only grow while the
    /// middle column still has its minimum — that is what used to push the right panel out.
    /// </summary>
    private void WadSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var available = ColumnGrid.ActualWidth - Gutters;
        if (available <= 0) return;
        WadColumn.Width = new GridLength(
            FitLeftColumn(available, WadColumn.ActualWidth + e.HorizontalChange, PreviewColumn.ActualWidth));
    }

    /// <summary>Right splitter: the right column may not take room the middle one needs.</summary>
    private void PreviewSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var available = ColumnGrid.ActualWidth - Gutters;
        if (available <= 0) return;
        PreviewColumn.Width = new GridLength(
            FitRightColumn(available, WadColumn.ActualWidth, PreviewColumn.ActualWidth - e.HorizontalChange));
    }

    /// <summary>Shrinking the window pulls the side columns back so the middle one keeps its width.</summary>
    private void ColumnGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var available = ColumnGrid.ActualWidth - Gutters;
        if (available <= 0) return;

        var wad = CurrentWidth(WadColumn);
        var preview = CurrentWidth(PreviewColumn);
        var wadTarget = FitLeftColumn(available, wad, preview);
        var previewTarget = FitRightColumn(available, wadTarget, preview);

        if (Math.Abs(wadTarget - wad) > 0.5) WadColumn.Width = new GridLength(wadTarget);
        if (Math.Abs(previewTarget - preview) > 0.5) PreviewColumn.Width = new GridLength(previewTarget);
    }

    /// <summary>
    /// Width the left column may take: at least <see cref="WadColumnMin"/>, at most what is left of
    /// <paramref name="available"/> once the middle column keeps its minimum and the right column
    /// keeps its current width.
    /// </summary>
    internal static double FitLeftColumn(double available, double wad, double preview) =>
        Math.Clamp(wad, WadColumnMin, Math.Max(WadColumnMin, available - TreeColumnMin - preview));

    /// <summary>Width the right column may take, holding the middle column at its minimum.</summary>
    internal static double FitRightColumn(double available, double wad, double preview) =>
        Math.Clamp(preview, PreviewColumnMin, Math.Max(PreviewColumnMin, available - TreeColumnMin - wad));

    /// <summary>Pixel width of a side column: its pixel request, or what it got when it is not pixel based.</summary>
    private static double CurrentWidth(ColumnDefinition column) =>
        column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;

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
