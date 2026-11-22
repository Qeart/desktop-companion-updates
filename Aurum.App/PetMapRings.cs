using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AionMeter.Core.PetMaps;
namespace Aurum;
public sealed class PetMapRings : Window
{
    private readonly Canvas _canvas = new();
    private readonly MatrixTransform _transform = new();
    private readonly SolidColorBrush _stroke = new(Color.FromRgb(255, 211, 105));
    private readonly SolidColorBrush _fill = new(Color.FromArgb(70, 255, 201, 83));
    private readonly List<(Ellipse Shape, double Radius)> _circles = [];
    private IReadOnlyList<PetSpawnArea>? _lastAreas;
    private int _lastMap;
    private double _sizedScale;
    internal bool ExcludedFromCapture { get; private set; }
    public PetMapRings()
    {
        _stroke.Freeze(); _fill.Freeze(); _canvas.RenderTransform = _transform;
        Title = "Aurum • obszary petów"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        IsHitTestVisible = false; Focusable = false; ResizeMode = ResizeMode.NoResize;
        var clip = new Grid { ClipToBounds = true }; clip.Children.Add(_canvas); Content = clip;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            // Desktop capture must see the terrain beneath our own markers.
            ExcludedFromCapture = Native.SetWindowDisplayAffinity(handle, 0x11);
        };
    }
    internal int Draw(PetWorldMap map, PetMapAlignment alignment, IReadOnlyList<PetSpawnArea> areas, Native.Point origin, int width, int height)
    {
        if (areas.Count == 0) { Hide(); return 0; }
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = width * .18; var top = height * .105; var right = width * .975; var bottom = height * .96;
        Left = (origin.X + left) / dpi.DpiScaleX; Top = (origin.Y + top) / dpi.DpiScaleY;
        Width = (right - left) / dpi.DpiScaleX; Height = (bottom - top) / dpi.DpiScaleY;
        if (_lastMap != map.Id || !ReferenceEquals(_lastAreas, areas))
        {
            _lastMap = map.Id; _lastAreas = areas; _sizedScale = 0; _canvas.Children.Clear(); _circles.Clear();
            foreach (var area in areas)
            {
                var centre = map.WorldToImage(area.XMetres, area.YMetres);
                var radius = area.RadiusMetres * 100 / (map.Bounds[2] - map.Bounds[0]) * map.Width;
                var circle = new Ellipse { Stroke = _stroke, Fill = _fill, IsHitTestVisible = false, Tag = centre };
                _circles.Add((circle, radius)); _canvas.Children.Add(circle);
            }
        }
        if (_sizedScale == 0 || Math.Abs(alignment.Scale - _sizedScale) > _sizedScale * .002)
        {
            _sizedScale = alignment.Scale;
            foreach (var (circle, radius) in _circles)
            {
                var size = radius;
                var centre = ((double X, double Y))circle.Tag;
                circle.Width = circle.Height = size * 2; circle.StrokeThickness = 2 * dpi.DpiScaleX / alignment.Scale;
                Canvas.SetLeft(circle, centre.X - size); Canvas.SetTop(circle, centre.Y - size);
            }
        }
        // Panning updates one compositor transform; it does not relayout every ellipse.
        _transform.Matrix = new Matrix(alignment.A / dpi.DpiScaleX, alignment.B / dpi.DpiScaleY,
            -alignment.B / dpi.DpiScaleX, alignment.A / dpi.DpiScaleY,
            (alignment.Tx - left) / dpi.DpiScaleX, (alignment.Ty - top) / dpi.DpiScaleY);
        if (!IsVisible) Show(); return _circles.Count;
    }
}
