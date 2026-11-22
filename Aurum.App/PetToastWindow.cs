using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Pets;

namespace Aurum;
public sealed class PetToastWindow : Window
{
    private readonly StackPanel _rows = new() { Width = 264, Margin = new Thickness(3) };
    private long _until;
    private double _baseHeight = 55;
    public PetToastWindow()
    {
        Title = "Aurum • postęp peta"; Width = 270; Height = _baseHeight;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; IsHitTestVisible = false;
        Focusable = false; ResizeMode = ResizeMode.NoResize; Content = _rows;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80 | 0x20);
            Native.SetWindowDisplayAffinity(hwnd, 0x11);
        };
    }
    public void Fill(IReadOnlyList<PetNotice> notices)
    {
        if (notices.Count == 0) return;
        _rows.Children.Clear();
        foreach (var notice in notices)
        {
            var row = PetAppearance.SoulNotice(notice); row.Margin = new Thickness(0, 0, 0, 4); _rows.Children.Add(row);
        }
        _baseHeight = 6 + _rows.Children.Count * 53;
        _until = Environment.TickCount64 + 7000;
    }
    public void Refresh(IReadOnlyList<PetNotice> notices, bool enabled)
    {
        Fill(notices);
        if (!enabled || Environment.TickCount64 > _until || _rows.Children.Count == 0) { Hide(); return; }
        var hwnd = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(hwnd, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            if (!process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase)) { Hide(); return; }
        }
        catch (Exception) { Hide(); return; }
        if (!Native.GetClientRect(hwnd, out var rect)) return;
        var point = new Native.Point(); if (!Native.ClientToScreen(hwnd, ref point)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = Math.Clamp(rect.Bottom / 1080d, .65, 1.75) / dpi.DpiScaleY;
        _rows.LayoutTransform = new ScaleTransform(scale, scale); Width = 270 * scale; Height = _baseHeight * scale;
        Left = (point.X + rect.Right * .50) / dpi.DpiScaleX - Width / 2;
        Top = (point.Y + rect.Bottom * .095) / dpi.DpiScaleY;
        if (!IsVisible) Show();
    }
    public void RenderSample(string path)
    {
        var pets = System.Text.Json.JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json")))!;
        var first = pets.First(p => p.Name == "Biruta"); var second = pets.First(p => p.Name == "Cave Bat");
        Fill([new(first.Name, 53, 2, 22, null, false, true, null, 1, first.Id), new(second.Name, 0, 3, 0, null, false, true, null, 1, second.Id)]);
        Height = _baseHeight; _rows.Measure(new Size(Width, Height)); _rows.Arrange(new Rect(0, 0, Width, Height));
        var target = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32); target.Render(_rows);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file); Close();
    }
}
