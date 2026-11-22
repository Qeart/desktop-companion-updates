using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.PetMaps;
using OpenCvSharp;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Aurum;

public sealed class OwnedMapSurface : FrameworkElement
{
    private BitmapSource? _image;
    private PetWorldMap? _map;
    private IReadOnlyList<PetSpawnArea> _areas = [];
    private PlayerMapPosition? _player;
    private bool _fresh;
    private Point? _drag;
    private (double X, double Y)? _anchor;
    private bool _fitPending;
    public MapViewport Camera { get; } = new();
    public bool FollowPlayer { get; set; }
    public event Action? CameraChanged;
    public int MapId => _map?.Id ?? 0;
    public int AreaCount => _areas.Count;
    public object DiagnosticState => new { MapId, Camera.Scale, Camera.X, Camera.Y, Areas = AreaCount, FollowPlayer,
        SharedScene = true, ScreenCapture = false,
        AnchorImage = _anchor is { } world ? new { world.X, world.Y } : null,
        AnchorScreen = _anchor is { } anchor ? new { X = Camera.Project(anchor.X, anchor.Y).X, Y = Camera.Project(anchor.X, anchor.Y).Y } : null };
    public OwnedMapSurface()
    {
        ClipToBounds = true; Focusable = true; Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
        SizeChanged += (_, _) => { if (_fitPending) Fit(); else InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) => {
            Focus(); var position = e.GetPosition(this);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { _anchor = Camera.Unproject(position.X, position.Y); InvalidateVisual(); return; }
            _drag = position; FollowPlayer = false; CaptureMouse(); Cursor = Cursors.ScrollAll; e.Handled = true;
        };
        MouseMove += (_, e) => {
            if (_drag is not { } previous) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            var point = e.GetPosition(this); Camera.Pan(point.X - previous.X, point.Y - previous.Y); _drag = point;
            Changed();
        };
        MouseLeftButtonUp += (_, _) => EndDrag(); LostMouseCapture += (_, _) => { _drag = null; Cursor = Cursors.Hand; };
        MouseWheel += (_, e) => { var point = e.GetPosition(this); Camera.Zoom(Math.Pow(1.18, e.Delta / 120d), point.X, point.Y); Changed(); e.Handled = true; };
    }
    private void EndDrag() { _drag = null; if (IsMouseCaptured) ReleaseMouseCapture(); Cursor = Cursors.Hand; }
    private void Changed() { InvalidateVisual(); CameraChanged?.Invoke(); }
    public void SetMap(PetWorldMap map)
    {
        if (_map?.Id == map.Id) return;
        _map = map; _anchor = null; _areas = [];
        using var bgr = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "data", "pet-maps", map.Id + ".webp"));
        if (bgr.Empty()) throw new IOException("Brak obrazu mapy: " + map.Name);
        using var bgra = new Mat(); Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        _image = BitmapSource.Create(bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, bgra.Data, checked((int)(bgra.Step() * bgra.Height)), (int)bgra.Step());
        _image.Freeze(); _fitPending = true; Fit();
    }
    public void SetAreas(IReadOnlyList<PetSpawnArea> areas) { _areas = areas; InvalidateVisual(); }
    public void SetPlayer(PlayerMapPosition? player, long now)
    {
        var fresh = player?.Fresh(now) == true;
        if (_player == player && _fresh == fresh) return;
        _player = player; _fresh = fresh;
        if (FollowPlayer && fresh) CenterPlayer();
        InvalidateVisual();
    }
    public bool CenterPlayer()
    {
        if (_map == null || _player?.MapId != _map.Id) return false;
        var point = _map.WorldToImage(_player.XMetres, _player.YMetres);
        Camera.Center(point.X, point.Y, ActualWidth, ActualHeight); Changed(); return true;
    }
    public void Fit()
    {
        if (_map == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        Camera.Fit(_map.Width, _map.Height, ActualWidth, ActualHeight); _fitPending = false; Changed();
    }
    public void Zoom(double factor) { Camera.Zoom(factor, ActualWidth / 2, ActualHeight / 2); Changed(); }
    public void FrameAreas()
    {
        if (_map == null || _areas.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var boxes = _areas.Select(area => {
            var centre = _map.WorldToImage(area.XMetres, area.YMetres);
            var radius = area.RadiusMetres * 100 * _map.Width / (_map.Bounds[2] - _map.Bounds[0]);
            return (Left: centre.X - radius, Right: centre.X + radius, Top: centre.Y - radius, Bottom: centre.Y + radius);
        }).ToArray();
        var left = boxes.Min(b => b.Left); var right = boxes.Max(b => b.Right); var top = boxes.Min(b => b.Top); var bottom = boxes.Max(b => b.Bottom);
        var scale = Math.Min(Math.Max(1, ActualWidth - 160) / Math.Max(80, right - left), Math.Max(1, ActualHeight - 140) / Math.Max(80, bottom - top));
        FollowPlayer = false; Camera.Zoom(scale / Camera.Scale, ActualWidth / 2, ActualHeight / 2);
        Camera.Center((left + right) / 2, (top + bottom) / 2, ActualWidth, ActualHeight); Changed();
    }
    public void SetTestAnchor(double imageX, double imageY) { _anchor = (imageX, imageY); InvalidateVisual(); }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 15, 20)), null, new Rect(RenderSize));
        if (_map == null || _image == null) return;
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        // The terrain and every world marker are submitted in the SAME drawing
        // context under this single camera transform. No separate overlay follows it.
        drawing.PushTransform(new MatrixTransform(Camera.Scale, 0, 0, Camera.Scale, Camera.X, Camera.Y));
        drawing.DrawImage(_image, new Rect(0, 0, _map.Width, _map.Height));
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(255, 211, 105)), 1.8 / Camera.Scale);
        var fill = new SolidColorBrush(Color.FromArgb(60, 255, 201, 83));
        foreach (var area in _areas)
        {
            var point = _map.WorldToImage(area.XMetres, area.YMetres);
            var rx = area.RadiusMetres * 100 * _map.Width / (_map.Bounds[2] - _map.Bounds[0]);
            var ry = area.RadiusMetres * 100 * _map.Height / (_map.Bounds[3] - _map.Bounds[1]);
            drawing.DrawEllipse(fill, stroke, new Point(point.X, point.Y), rx, ry);
        }
        if (_player?.MapId == _map.Id)
        {
            var point = _map.WorldToImage(_player.XMetres, _player.YMetres);
            var at = new Point(point.X, point.Y); var radius = 7 / Camera.Scale;
            drawing.PushOpacity(_fresh ? 1 : .38);
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(65, 255, 211, 105)), null, at, radius * 2.4, radius * 2.4);
            drawing.DrawEllipse(Brushes.Black, new Pen(Brushes.White, 1.5 / Camera.Scale), at, radius * 1.25, radius * 1.25);
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 211, 105)), null, at, radius, radius);
            drawing.DrawEllipse(Brushes.White, null, at, radius * .28, radius * .28); drawing.Pop();
        }
        if (_anchor is { } anchor)
        {
            var at = new Point(anchor.X, anchor.Y); var pen = new Pen(Brushes.Magenta, 2 / Camera.Scale); var r = 12 / Camera.Scale;
            drawing.DrawLine(pen, new Point(at.X - r, at.Y), new Point(at.X + r, at.Y));
            drawing.DrawLine(pen, new Point(at.X, at.Y - r), new Point(at.X, at.Y + r));
        }
        drawing.Pop(); drawing.Pop();
    }
}
