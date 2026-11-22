using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using AionMeter.Core.Cubes;
using AionMeter.Core.PetMaps;

namespace Aurum;

internal readonly record struct GameViewport(nint Handle, int X, int Y, int Width, int Height);
internal static class GameWindowAccess
{
    private static uint _gamePid;
    internal static bool GameOrAurumForeground()
    {
        var handle = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(handle, out var pid);
        return pid == Environment.ProcessId || IsGame(pid);
    }
    private static bool IsGame(uint pid)
    {
        if (pid == 0) return false;
        if (pid == _gamePid) return true;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase)) return false;
            _gamePid = pid; return true;
        }
        catch (Exception) { return false; }
    }
    internal static GameViewport? ForegroundGame()
    {
        var handle = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(handle, out var pid);
        if (!IsGame(pid) || !Native.GetClientRect(handle, out var rect) || rect.Right < 640 || rect.Bottom < 480) return null;
        var origin = new Native.Point(); if (!Native.ClientToScreen(handle, ref origin)) return null;
        return new(handle, origin.X, origin.Y, rect.Right, rect.Bottom);
    }
}

internal sealed class CubeCameraReader
{
    private sealed class Calibration
    {
        public Calibration() { }
        public double MapRight { get; set; } = 271;
        public double MapTop { get; set; } = 100;
        public double MapWidth { get; set; } = 250;
        public double MapHeight { get; set; } = 165;
        public double CentreX { get; set; } = 124;
        public double CentreY { get; set; } = 82;
        public double InnerRadius { get; set; } = 28;
        public double OuterRadius { get; set; } = 74;
        public double SectorWidth { get; set; } = 110;
        public bool Valid => MapRight is > 50 and < 700 && MapTop is >= 0 and < 500 && MapWidth is >= 50 and <= 500
            && MapHeight is >= 50 and <= 500 && InnerRadius >= 10 && OuterRadius > InnerRadius && OuterRadius <= 150
            && CentreX - OuterRadius >= 0 && CentreY - OuterRadius >= 0 && CentreX + OuterRadius < MapWidth
            && CentreY + OuterRadius < MapHeight && SectorWidth is >= 40 and <= 160;
    }
    private readonly Calibration _calibration;
    internal CubeCameraReader()
    {
        _calibration = new();
        var legacy = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Aion2-Cube-Arrow", "settings.json"));
        try { if (File.Exists(legacy) && JsonSerializer.Deserialize<Calibration>(File.ReadAllText(legacy)) is { Valid: true } saved) _calibration = saved; }
        catch (Exception error) when (error is IOException or JsonException) { }
    }
    internal CameraReading? Read(GameViewport game)
    {
        var scale = game.Height / 1080d; var bounds = MapBounds(game);
        if (!new Rectangle(game.X, game.Y, game.Width, game.Height).Contains(bounds)) return null;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
        return ReadMap(bitmap, scale);
    }
    private Rectangle MapBounds(GameViewport game)
    {
        var c = _calibration; var scale = game.Height / 1080d;
        return new Rectangle(game.X + game.Width - (int)(c.MapRight * scale), game.Y + (int)(c.MapTop * scale),
            (int)(c.MapWidth * scale), (int)(c.MapHeight * scale));
    }
    internal CameraReading? ReadFile(string path)
    {
        using var image = new Bitmap(path);
        using var bitmap = image.Clone(MapBounds(new(0, 0, 0, image.Width, image.Height)), PixelFormat.Format32bppArgb);
        return ReadMap(bitmap, image.Height / 1080d);
    }
    private CameraReading? ReadMap(Bitmap bitmap, double scale)
    {
        var c = _calibration;
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            var scores = Navigation.EdgeScores((x, y) => {
                if (x < 0 || y < 0 || x >= data.Width || y >= data.Height) return (0, 0, 0);
                var offset = y * data.Stride + x * 4; return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            }, c.CentreX * scale, c.CentreY * scale, c.InnerRadius * scale, c.OuterRadius * scale);
            return Navigation.DetectCamera(scores, c.SectorWidth);
        }
        finally { bitmap.UnlockBits(data); }
    }
}

internal sealed class HiddenCubeController : IDisposable
{
    private readonly UserSettings _settings;
    private readonly HiddenCubeTracker _tracker;
    private readonly Func<PlayerMapPosition?> _position;
    private readonly Func<bool> _suppressed;
    private readonly CubeCameraReader _camera = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CubeCompassWindow? _window;
    private bool _busy, _disposed;
    private HiddenCubeTarget? _target;
    private CameraReading? _heading;
    private long _readMs;
    public string Status { get; private set; } = "Szukam skrzynki w pobliżu";
    public object DiagnosticState => new { Enabled = _settings.ShowHiddenCube, Visible = _window?.IsVisible == true,
        Status, Cubes = _tracker.Count, Target = _target, Camera = _heading, CameraReadMilliseconds = _readMs, SharedCapture = true, Backend = "Aurum" };
    internal HiddenCubeController(UserSettings settings, HiddenCubeTracker tracker, Func<PlayerMapPosition?> position, Func<bool> suppressed)
    {
        _settings = settings; _tracker = tracker; _position = position; _suppressed = suppressed;
        _timer.Tick += async (_, _) => await Tick(); _timer.Start();
    }
    internal void RefreshEnabled()
    {
        if (!_settings.ShowHiddenCube) { _window?.Hide(); Status = "Wskaźnik wyłączony"; }
        else { Status = "Szukam skrzynki w pobliżu"; _ = Tick(); }
    }
    private async Task Tick()
    {
        if (_disposed) return;
        if (!_settings.ShowHiddenCube || _suppressed()) { _window?.Hide(); Status = _settings.ShowHiddenCube ? "Wstrzymany podczas otwartej mapy lub sklepu" : "Wskaźnik wyłączony"; return; }
        var game = GameWindowAccess.ForegroundGame();
        if (game == null) { _window?.Hide(); return; }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var range = double.IsFinite(_settings.CubeNearbyMetres) ? Math.Clamp(_settings.CubeNearbyMetres, 1, 250) : 100;
        _target = _tracker.Nearest(_position(), now, range);
        if (_target == null) { _window?.Hide(); Status = _tracker.Count > 0 && _position()?.Fresh(now) != true ? "Odśwież pozycję umiejętnością" : "Szukam skrzynki w pobliżu"; return; }
        if (_busy) return;
        _busy = true;
        try
        {
            var watch = Stopwatch.StartNew(); _heading = await Task.Run(() => _camera.Read(game.Value)); _readMs = watch.ElapsedMilliseconds;
            if (_disposed) return;
            if (!_settings.ShowHiddenCube || _suppressed() || Native.GetForegroundWindow() != game.Value.Handle) { _window?.Hide(); return; }
            _target = _tracker.Nearest(_position(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), range);
            if (_heading is not { } heading || _target is not { } target)
            { _window?.Hide(); Status = _target == null ? "Szukam skrzynki w pobliżu" : "Skrzynka wykryta · czekam na kierunek kamery"; return; }
            _window ??= new CubeCompassWindow();
            _window.UpdateTarget(target, Navigation.Relative(target.Bearing, heading.Heading), game.Value);
            Status = $"Skrzynka {target.Metres:0} m od Ciebie";
        }
        catch (Exception error)
        {
            if (_disposed) return;
            _window?.Hide(); Status = "Odczyt kierunku chwilowo niedostępny";
            try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "cube-error.txt"), error.ToString()); } catch (IOException) { }
        }
        finally { _busy = false; }
    }
    public void Dispose() { _disposed = true; _timer.Stop(); _window?.Close(); }
}
