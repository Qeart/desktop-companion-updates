using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AionMeter.Core.Cubes;

namespace Aurum;

internal sealed class CubeCompassVisual : FrameworkElement
{
    internal static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double), typeof(CubeCompassVisual), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty PulseProperty = DependencyProperty.Register(nameof(Pulse), typeof(double), typeof(CubeCompassVisual), new FrameworkPropertyMetadata(.5d, FrameworkPropertyMetadataOptions.AffectsRender));
    internal double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    internal double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    private static readonly Brush Gold = PetAppearance.Ink("#F6D48A"), Muted = PetAppearance.Ink("#D6C6A3");
    private static readonly Geometry Needle = Geometry.Parse("M72,7 L82,34 L72,28 L62,34 Z");
    private static readonly Geometry ForwardArc = Geometry.Parse("M38,23 A51,51 0 0 1 106,23");
    private static readonly Geometry Cube = Geometry.Parse("M72,48 L84,55 L84,69 L72,76 L60,69 L60,55 Z M60,55 L72,62 L84,55 M72,62 L72,76");
    private static readonly Brush NeedleFill = new LinearGradientBrush(Color.FromRgb(255, 242, 190), Color.FromRgb(202, 143, 40), 90);
    private static readonly Brush Halo = new RadialGradientBrush(new GradientStopCollection { new(Color.FromArgb(45, 239, 191, 92), .3), new(Color.FromArgb(0, 239, 191, 92), 1) });
    private static readonly DrawingGroup Dial = BuildDial();
    private HiddenCubeTarget _target = new(0, 12, 0, 0);
    private static DrawingGroup BuildDial()
    {
        var group = new DrawingGroup(); using (var drawing = group.Open())
        {
            drawing.DrawEllipse(PetAppearance.Ink("#E20D1014"), new Pen(PetAppearance.Ink("#B09050"), 1), new Point(72, 61), 37, 37);
            drawing.DrawEllipse(null, new Pen(PetAppearance.Ink("#625239"), .7), new Point(72, 61), 43, 43);
            for (var i = 0; i < 24; i++)
            {
                var angle = i * Math.PI / 12; var outer = i % 6 == 0 ? 49d : 47;
                drawing.DrawLine(new Pen(PetAppearance.Ink(i % 6 == 0 ? "#DDC38C" : "#6C6148"), i % 6 == 0 ? 1.5 : .8),
                    new Point(72 + Math.Sin(angle) * 44, 61 - Math.Cos(angle) * 44), new Point(72 + Math.Sin(angle) * outer, 61 - Math.Cos(angle) * outer));
            }
        }
        group.Freeze(); return group;
    }
    internal void Update(HiddenCubeTarget target, double angle, bool animate = true)
    {
        _target = target;
        var current = Angle; var destination = current + Navigation.Relative(angle, current);
        if (animate) BeginAnimation(AngleProperty, new DoubleAnimation(current, destination, TimeSpan.FromMilliseconds(80)));
        else { BeginAnimation(AngleProperty, null); Angle = destination; }
        InvalidateVisual();
    }
    internal void Animate(bool enabled)
    {
        BeginAnimation(PulseProperty, enabled ? new DoubleAnimation(.30, .75, TimeSpan.FromSeconds(1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.PushOpacity(Pulse); drawing.DrawEllipse(Halo, null, new Point(72, 61), 62, 62); drawing.Pop();
        drawing.DrawDrawing(Dial);
        drawing.PushTransform(new RotateTransform(Angle, 72, 61));
        drawing.DrawGeometry(null, new Pen(Gold, 2), ForwardArc);
        drawing.DrawGeometry(NeedleFill, new Pen(PetAppearance.Ink("#FFF0BC"), .7), Needle); drawing.Pop();
        drawing.DrawGeometry(PetAppearance.Ink("#302719"), new Pen(Gold, 1.4), Cube);
        Text(drawing, $"{_target.Metres:0} m", 23, Gold, 111, FontWeights.SemiBold);
        Text(drawing, "HIDDEN CUBE", 8.5, Muted, 138, FontWeights.SemiBold);
        if (Math.Abs(_target.HeightMetres) >= 6) Text(drawing, $"{(_target.HeightMetres > 0 ? "↑" : "↓")} {Math.Abs(_target.HeightMetres):0} m", 10, Muted, 154, FontWeights.Normal);
    }
    private void Text(DrawingContext drawing, string value, double size, Brush brush, double top, FontWeight weight)
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("pl-PL"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var at = new Point(72 - text.Width / 2, top);
        drawing.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2.4), text.BuildGeometry(at));
        drawing.DrawText(text, at);
    }
    internal void RenderSample(string output)
    {
        Update(new(1, 18, 0, 8), 42, false); Measure(new Size(144, 174)); Arrange(new Rect(0, 0, 144, 174));
        var target = new RenderTargetBitmap(144, 174, 96, 96, PixelFormats.Pbgra32); target.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using var file = File.Create(output); encoder.Save(file);
    }
}

internal sealed class CubeCompassWindow : Window
{
    private readonly CubeCompassVisual _visual = new() { Width = 144, Height = 174 };
    internal CubeCompassWindow()
    {
        Title = "Aurum • Hidden Cube"; Width = 144; Height = 174; Content = _visual;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = false; Focusable = false;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            Native.SetWindowDisplayAffinity(handle, 0x11);
        };
        IsVisibleChanged += (_, _) => _visual.Animate(IsVisible);
        Closed += (_, _) => _visual.Animate(false);
    }
    internal void UpdateTarget(HiddenCubeTarget target, double relative, GameViewport game)
    {
        var dpi = VisualTreeHelper.GetDpi(this); var scale = Math.Clamp(game.Height / 1080d, .65, 1.5) / dpi.DpiScaleY;
        Width = 144 * scale; Height = 174 * scale; _visual.LayoutTransform = new ScaleTransform(scale, scale);
        Left = (game.X + game.Width * .5) / dpi.DpiScaleX - Width / 2;
        Top = (game.Y + game.Height * .20) / dpi.DpiScaleY;
        _visual.Update(target, relative, IsVisible);
        if (!IsVisible) Show();
    }
}
