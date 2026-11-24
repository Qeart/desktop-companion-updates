using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Bosses;

namespace Aurum;

// Two independent transparent surfaces: DBM-style timer bars and a central warning.
// Neither has a surrounding window, brand header or opaque panel.
internal sealed class BossOverlayWindow : Window
{
    private readonly UserSettings _settings;
    private readonly BossOverlayVisual _visual;
    private readonly bool _warning;
    private nint _handle;
    private Point? _drag;
    private double _left, _top;
    internal BossOverlayWindow(UserSettings settings, bool warning = false)
    {
        _settings = settings; _warning = warning; _visual = warning ? new BossWarningVisual() : new BossBarsVisual();
        Title = warning ? "Aurum • ostrzeżenie bossa" : "Aurum • timery bossa"; Icon = AurumBrand.Image;
        AllowsTransparency = true; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Content = _visual;
        var area = SystemParameters.WorkArea;
        Left = warning ? settings.BossWarningLeft ?? area.Left + (area.Width - 720) / 2 : settings.BossLeft ?? area.Left + area.Width * .72;
        Top = warning ? settings.BossWarningTop ?? area.Top + area.Height * .26 : settings.BossTop ?? area.Top + 160;
        SourceInitialized += (_, _) => { _handle = new WindowInteropHelper(this).Handle; ApplyOptions(); ClampPosition(); };
        MouseLeftButtonDown += (_, e) => {
            if (_settings.BossOverlayLocked) return;
            _drag = PointToScreen(e.GetPosition(this)); _left = Left; _top = Top; CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, e) => {
            if (_drag is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var p = PointToScreen(e.GetPosition(this)); var dpi = VisualTreeHelper.GetDpi(this);
            Left = _left + (p.X - start.X) / dpi.DpiScaleX; Top = _top + (p.Y - start.Y) / dpi.DpiScaleY;
        };
        MouseLeftButtonUp += (_, _) => {
            if (_drag == null) return; _drag = null; ReleaseMouseCapture(); ClampPosition();
            if (_warning) { _settings.BossWarningLeft = Left; _settings.BossWarningTop = Top; }
            else { _settings.BossLeft = Left; _settings.BossTop = Top; } _settings.Save();
        };
        LostMouseCapture += (_, _) => _drag = null;
    }
    private void ClampPosition()
    {
        var l = SystemParameters.VirtualScreenLeft; var t = SystemParameters.VirtualScreenTop;
        Left = double.IsFinite(Left) ? Math.Clamp(Left, l, Math.Max(l, l + SystemParameters.VirtualScreenWidth - Width)) : l;
        Top = double.IsFinite(Top) ? Math.Clamp(Top, t, Math.Max(t, t + SystemParameters.VirtualScreenHeight - Height)) : t;
    }
    internal void ApplyOptions()
    {
        var width = _warning ? 720 : Math.Clamp(double.IsFinite(_settings.BossWidth) ? _settings.BossWidth : 360, 280, 560);
        Width = Math.Min(width * Scale, SystemParameters.WorkArea.Width);
        Opacity = Math.Clamp(double.IsFinite(_settings.BossOpacity) ? _settings.BossOpacity : .95, .35, 1);
        _visual.LayoutTransform = new ScaleTransform(Scale, Scale);
        Cursor = _settings.BossOverlayLocked ? Cursors.Arrow : Cursors.SizeAll;
        if (_handle != 0) {
            var style = Native.GetWindowLongPtr(_handle, -20) | 0x08000000 | 0x80;
            Native.SetWindowLongPtr(_handle, -20, _settings.BossOverlayLocked ? style | 0x20 : style & ~0x20);
        }
    }
    private double Scale => Math.Clamp(double.IsFinite(_settings.BossScale) ? _settings.BossScale : 1, .7, 1.6);
    internal void Update(BossAssistantSnapshot snapshot, string mode, bool placement = false)
    {
        ApplyOptions(); _visual.State = snapshot; _visual.Mode = mode; _visual.Placement = placement;
        Height = _visual.DesiredHeight * Scale; _visual.InvalidateVisual();
    }
    internal void RenderPreview(BossAssistantSnapshot snapshot, string mode, string file)
    {
        Update(snapshot, mode); var size = new Size(Width, Height);
        _visual.Measure(size); _visual.Arrange(new Rect(size)); _visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}

internal abstract class BossOverlayVisual : FrameworkElement
{
    internal BossAssistantSnapshot State = new([], [], [], 0);
    internal string Mode = "LIVE";
    internal bool Placement;
    internal abstract double DesiredHeight { get; }
    protected static Brush Ink(string hex) => PetAppearance.Ink(hex);
    protected static Brush ColorFor(string category) => Ink(category switch { "danger" => "#F27262", "support" => "#6CB89B", "info" => "#77ACCE", _ => "#DBAC50" });
    protected void Text(DrawingContext d, string value, double x, double y, double width, double size, Brush color, bool bold = true, TextAlignment align = TextAlignment.Left, int lines = 1)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = lines, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = align };
        var outline = text.BuildGeometry(new Point(x, y));
        d.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, size > 20 ? 3 : 2) { LineJoin = PenLineJoin.Round }, outline);
        d.DrawText(text, new Point(x, y));
    }
}

internal sealed class BossBarsVisual : BossOverlayVisual
{
    private double Header => Mode == "LIVE" ? 0 : 18;
    private int TimerRows => State.Bars.Count > 0 ? Math.Min(6, State.Bars.Count) : Placement && State.ObservedSkills.Count == 0 ? 2 : 0;
    internal override double DesiredHeight => Math.Max(1, Header + TimerRows * 35 +
        (State.ObservedSkills.Count > 0 ? 19 + Math.Min(4, State.ObservedSkills.Count) * 30 : 0) + (Placement ? 24 : 0));
    protected override void OnRender(DrawingContext d)
    {
        var w = ActualWidth; var y = Header;
        if (Placement) d.DrawRoundedRectangle(Ink("#15151515"), new Pen(Ink("#BFA06B"), 1) { DashStyle = DashStyles.Dash }, new Rect(.5, .5, Math.Max(1, w - 1), DesiredHeight - 1), 3, 3);
        if (Mode != "LIVE") Text(d, Mode == "DEMO" ? "DEMO · DANE TESTOWE" : "REPLAY · ZAPIS WALKI", 34, 0, w - 35, 9, Ink("#DEC6FF"));
        foreach (var bar in State.Bars.Take(6)) {
            var urgent = bar.Seconds <= 5 && bar.Kind != "cast";
            var color = ColorFor(urgent ? "danger" : bar.Category);
            d.DrawRectangle(Ink("#EA080A0D"), new Pen(Brushes.Black, 2), new Rect(33, y + 1, w - 35, 28));
            if (bar.Fraction > 0) {
                d.DrawRectangle(color, null, new Rect(34, y + 2, (w - 37) * bar.Fraction, 26));
                d.DrawRectangle(new LinearGradientBrush(Color.FromArgb(65, 255, 255, 255), Color.FromArgb(80, 0, 0, 0), 90), null, new Rect(34, y + 2, (w - 37) * bar.Fraction, 26));
            }
            d.DrawRectangle(Ink("#25211B"), new Pen(Ink("#AF9154"), 1), new Rect(1, y + 1, 28, 28));
            Text(d, bar.Kind == "cast" ? "✦" : "!", 2, y + 2, 26, 19, color, true, TextAlignment.Center);
            var prefix = bar.Kind == "cast" ? "CAST · " : bar.Kind == "windup" ? "PRZYGOT. · " : "≈ ";
            Text(d, prefix + bar.Label, 39, y + 5, w - 103, 12, Ink("#FFF8E9"));
            Text(d, (bar.Kind == "windup" ? "≈" : "") + bar.Seconds.ToString("0.0", CultureInfo.InvariantCulture), w - 61, y + 4, 52, 14, Ink(urgent ? "#FFE5D4" : "#FFFFFF"), true, TextAlignment.Right);
            y += 35;
        }
        if (Placement && State.Bars.Count == 0 && State.ObservedSkills.Count == 0) {
            DrawSample(d, w, y, "≈ Następna mechanika", "12.5", .65, "warning");
            DrawSample(d, w, y + 35, "≈ Pilna mechanika", "3.0", .18, "danger");
            y += 70;
        }
        if (State.ObservedSkills.Count > 0) {
            Text(d, "OSTATNIE UŻYCIA", 34, y + 1, w - 35, 9, Ink("#B2C2D3")); y += 19;
            foreach (var skill in State.ObservedSkills.Take(4)) {
                d.DrawRectangle(Ink("#DA101923"), new Pen(Ink("#62778C"), .7), new Rect(33, y + 1, w - 35, 24));
                d.DrawRectangle(Ink("#162330"), new Pen(Ink("#62778C"), 1), new Rect(3, y + 1, 24, 24));
                Text(d, "◆", 4, y + 3, 22, 14, Ink("#A4C9EA"), true, TextAlignment.Center);
                Text(d, skill.Name, 39, y + 4, w - 106, 12, Ink("#F1F5FA"));
                Text(d, "ODCZYT", w - 61, y + 7, 52, 8, Ink("#B3C9DE"), true, TextAlignment.Right);
                y += 30;
            }
        }
        if (Placement) Text(d, "PASKI · PRZECIĄGNIJ", 34, DesiredHeight - 20, w - 35, 9, Ink("#F4D894"));
    }
    private void DrawSample(DrawingContext d, double w, double y, string label, string seconds, double fraction, string category)
    {
        d.DrawRectangle(Ink("#EA080A0D"), new Pen(Brushes.Black, 2), new Rect(33, y + 1, w - 35, 28));
        d.DrawRectangle(ColorFor(category), null, new Rect(34, y + 2, (w - 37) * fraction, 26));
        d.DrawRectangle(Ink("#25211B"), new Pen(Ink("#AF9154"), 1), new Rect(1, y + 1, 28, 28));
        Text(d, "!", 2, y + 2, 26, 19, ColorFor(category), true, TextAlignment.Center);
        Text(d, label, 39, y + 5, w - 103, 12, Brushes.White); Text(d, seconds, w - 61, y + 4, 52, 14, Brushes.White, true, TextAlignment.Right);
    }
}

internal sealed class BossWarningVisual : BossOverlayVisual
{
    internal override double DesiredHeight => 100;
    protected override void OnRender(DrawingContext d)
    {
        var w = ActualWidth; var alert = State.Alerts.LastOrDefault();
        if (Placement) d.DrawRectangle(Ink("#15151515"), new Pen(Ink("#BFA06B"), 1) { DashStyle = DashStyles.Dash }, new Rect(.5, .5, Math.Max(1, w - 1), 99));
        if (alert == null && !Placement) return;
        var label = Mode != "LIVE" ? Mode + " · " : "";
        label += alert?.Predicted == true ? "PRZEWIDYWANA MECHANIKA" : "";
        if (Placement && alert == null) label += "OSTRZEŻENIE · PRZECIĄGNIJ";
        if (label.Length > 0) Text(d, label, 7, 0, w - 14, 10, Ink("#EFD9A8"), true, TextAlignment.Center);
        Text(d, alert?.Text ?? "Przygotuj się na mechanikę!", 7, 21, w - 14, 28, ColorFor(alert?.Category ?? "danger"), true, TextAlignment.Center, 2);
    }
}
