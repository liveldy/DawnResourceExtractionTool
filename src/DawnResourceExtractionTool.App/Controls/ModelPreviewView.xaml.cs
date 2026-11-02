using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DawnResourceExtractionTool.App.Controls;

/// <summary>Hosts a model in a Helix viewport and frames it whenever the model changes.</summary>
public partial class ModelPreviewView : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(Model3D),
        typeof(ModelPreviewView),
        new PropertyMetadata(null, OnModelChanged));

    public ModelPreviewView()
    {
        InitializeComponent();
        ConfigureInteraction();
    }

    public Model3D? Model
    {
        get => (Model3D?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>
    /// Left drag turns the model, right drag moves it and the wheel zooms. Helix defaults to
    /// right-drag rotation with Shift+right for panning, which nobody expects in a viewer, and
    /// its sensitivity is a multiplier (1 = default), so the viewport keeps the library scale.
    /// </summary>
    private void ConfigureInteraction()
    {
        Viewport.RotateGesture = new MouseGesture(MouseAction.LeftClick);
        Viewport.PanGesture = new MouseGesture(MouseAction.RightClick);
    }

    private void ResetViewClick(object sender, RoutedEventArgs e) => FrameModel();

    /// <summary>Frames the model from a three-quarter front view; the default is top-down.</summary>
    private void FrameModel()
    {
        if (Visual.Content is not Model3D model) return;

        var bounds = model.Bounds;
        if (bounds.IsEmpty || bounds.SizeX + bounds.SizeY + bounds.SizeZ <= 0)
        {
            Viewport.ZoomExtents(0);
            return;
        }

        var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var radius = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        if (radius <= 0) radius = 1;

        var direction = new Vector3D(0.5, 0.28, 1.0);
        direction.Normalize();

        // Look at the model from the front, then let Helix fit the bounds along that axis.
        Viewport.Camera = new PerspectiveCamera(center + direction * (radius * 2), -direction, new Vector3D(0, 1, 0), 45);
        Viewport.ZoomExtents(bounds, 0);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ModelPreviewView)d;

        // A Model3D is a Freezable: posing it (MeshGeometry3D.Positions and .Normals are rewritten
        // on every animation frame) raises this callback again with the very same instance. Only a
        // new model should re-frame the camera, otherwise the view snaps back 30 times a second and
        // the user cannot orbit the model while the animation runs.
        if (ReferenceEquals(e.OldValue, e.NewValue)) return;

        view.Visual.Content = e.NewValue as Model3D;
        view.Hint.Visibility = e.NewValue is null ? Visibility.Visible : Visibility.Collapsed;
        view.ResetViewButton.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        if (e.NewValue is null) return;

        // Frame the model once the viewport has a real size: a three-quarter view from
        // the front reads much better than the top-down default of ZoomExtents.
        view.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (view.ActualWidth <= 0 || view.ActualHeight <= 0) return;
            view.FrameModel();
        }), DispatcherPriority.Loaded);
    }
}
