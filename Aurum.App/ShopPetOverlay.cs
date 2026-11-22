using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Pets;

namespace Aurum;
public sealed class ShopPetOverlay : Window
{
    private readonly Canvas _canvas = new();
    private readonly ShopScreenReader _reader;
    private readonly PetProgress _progress;
    private readonly PetDefinition[] _pets;
    private bool _armed, _busy, _closed, _enabled;
    private long _lastRead, _lastStatus;
    private int _generation, _statusBusy;
    private uint _gameProcess;
    private string _foreground = "", _layoutKey = "";
    public bool HasShopView => _armed;
    public object DiagnosticState => new { Armed = _armed, Busy = _busy, Visible = IsVisible, Stage = _reader.Stage,
        LastReadMsAgo = Environment.TickCount64 - _lastRead, Foreground = _foreground, Reader = _reader.DiagnosticState };
    public ShopPetOverlay(PetProgress progress)
    {
        _progress = progress;
        _pets = JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"))) ?? [];
        _reader = new(_pets);
        _reader.Updated += () => Dispatcher.BeginInvoke(() => { if (!_closed) { _lastRead = 0; Refresh(_enabled); } });
        Title = "Aurum • pety w sklepie"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        IsHitTestVisible = false; Focusable = false; ResizeMode = ResizeMode.NoResize; Content = _canvas;
        Width = Height = 1;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            Native.SetWindowDisplayAffinity(handle, 0x11);
        };
        Closed += (_, _) => { _closed = true; _generation++; _reader.Dispose(); };
    }
    public void Arm() { _armed = true; _generation++; _lastRead = 0; _reader.RequestScan(); }
    public void Stop() { _armed = false; _generation++; Hide(); }
    public async void Refresh(bool enabled)
    {
        if (_closed) return;
        if (_enabled != enabled) { _enabled = enabled; _generation++; }
        if (!enabled) { if (IsVisible) Hide(); return; }
        var handle = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(handle, out var id);
        try
        {
            if (id != _gameProcess)
            {
                using var process = Process.GetProcessById((int)id); _foreground = process.ProcessName;
                if (!process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase)) { Stop(); return; }
                _gameProcess = id;
            }
            _foreground = "AION2";
        }
        catch (Exception error) { _foreground = error.GetType().Name; Stop(); return; }
        if (_busy || Environment.TickCount64 - _lastRead < 90) return;
        if (!Native.GetClientRect(handle, out var bounds) || bounds.Right < 200 || bounds.Bottom < 200) return;
        var point = new Native.Point(); if (!Native.ClientToScreen(handle, ref point)) return;
        _busy = true; _lastRead = Environment.TickCount64; var generation = _generation;
        try
        {
            // This task only captures and checks pixels; OCR has its own background task.
            var result = await Task.Run(() => _reader.Poll(point.X, point.Y, bounds.Right, bounds.Bottom));
            if (_closed || generation != _generation) return;
            if (Native.GetForegroundWindow() != handle || !_enabled) { Hide(); return; }
            if (!Native.GetClientRect(handle, out var current) || current.Right != bounds.Right || current.Bottom != bounds.Bottom) { Hide(); return; }
            _armed = result.IsShop;
            if (!result.IsShop || result.Cards.Count == 0) { Hide(); WriteStatus(result, []); return; }
            var key = _progress.Version + ":" + bounds.Right + ":" + bounds.Bottom + ":" + point.X + ":" + point.Y + ":"
                + string.Join("|", result.Cards.Select(r => $"{r.Text}:{r.X:F1}:{r.Y:F1}"));
            if (key != _layoutKey)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var matches = BuildBadges(_canvas, result.Cards, bounds.Bottom / 1080d / dpi.DpiScaleX, bounds.Bottom / 1080d / dpi.DpiScaleY,
                    bounds.Bottom / 1080d, dpi.DpiScaleX, dpi.DpiScaleY);
                FitToBadges(point, dpi); _layoutKey = key; WriteStatus(result, matches, force: true);
            }
            else if (!IsVisible && _canvas.Children.Count > 0) Show();
            WriteStatus(result, []);
        }
        catch (Exception error)
        {
            if (!_closed) Hide();
            _ = Task.Run(() => { try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "shop-error.txt"), error.ToString()); } catch (IOException) { } });
        }
        finally { _busy = false; }
    }
    private List<object> BuildBadges(Canvas canvas, IReadOnlyList<ShopText> cards, double sx, double sy, double gameScale, double dpiX, double dpiY)
    {
        canvas.Children.Clear(); var matches = new List<object>();
        foreach (var title in cards)
        {
            var pet = ShopPetMatcher.Match(title.Text, _pets); if (pet == null) continue;
            var state = _progress.State(pet.Id);
            var badge = new Viewbox { Width = PetAppearance.ShopBadgeWidth * sx, Height = PetAppearance.ShopBadgeHeight * sy,
                Stretch = Stretch.Fill, Child = PetAppearance.ShopBadge(state) };
            Canvas.SetLeft(badge, (title.X + title.Width / 2) / dpiX - badge.Width / 2);
            Canvas.SetTop(badge, (title.Y + title.Height + 25 * gameScale) / dpiY);
            canvas.Children.Add(badge); matches.Add(new { pet.Id, pet.Name, Count = ShopPetMatcher.Counter(state), title.X, title.Y });
        }
        return matches;
    }
    private void FitToBadges(Native.Point point, DpiScale dpi)
    {
        var labels = _canvas.Children.Cast<FrameworkElement>().ToArray();
        if (labels.Length == 0) { Hide(); return; }
        var x = labels.Min(Canvas.GetLeft); var y = labels.Min(Canvas.GetTop);
        var right = labels.Max(l => Canvas.GetLeft(l) + l.Width); var bottom = labels.Max(l => Canvas.GetTop(l) + l.Height);
        foreach (var label in labels) { Canvas.SetLeft(label, Canvas.GetLeft(label) - x + 2); Canvas.SetTop(label, Canvas.GetTop(label) - y + 2); }
        Left = point.X / dpi.DpiScaleX + x - 2; Top = point.Y / dpi.DpiScaleY + y - 2;
        Width = right - x + 4; Height = bottom - y + 4;
        if (!IsVisible) Show();
    }
    private void WriteStatus(ShopReading result, List<object> matches, bool force = false)
    {
        if (!force && Environment.TickCount64 - _lastStatus < 2000 || Interlocked.CompareExchange(ref _statusBusy, 1, 0) != 0) return;
        _lastStatus = Environment.TickCount64;
        var json = JsonSerializer.Serialize(new { UpdatedAt = DateTimeOffset.UtcNow, result.IsShop, result.Mode, result.Milliseconds,
            result.Header, Matches = matches, Reader = _reader.DiagnosticState });
        _ = Task.Run(() =>
        {
            try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "shop-status.json"), json); }
            catch (IOException) { }
            finally { Interlocked.Exchange(ref _statusBusy, 0); }
        });
    }
    internal void RenderSample(string background, IReadOnlyList<ShopText> cards, string output)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(background)); image.EndInit(); image.Freeze();
        var canvas = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = new ImageBrush(image) { Stretch = Stretch.Fill } };
        BuildBadges(canvas, cards, image.PixelHeight / 1080d, image.PixelHeight / 1080d, image.PixelHeight / 1080d, 1, 1);
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); target.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(output); encoder.Save(file);
    }
}
