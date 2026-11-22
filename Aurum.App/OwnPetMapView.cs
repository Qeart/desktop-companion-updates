using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;

namespace Aurum;

internal sealed class OwnPetMapView : IPetMapView
{
    private readonly PetProgress _progress;
    private readonly UserSettings _settings;
    private readonly Func<int> _map;
    private readonly Func<PlayerMapPosition?> _position;
    private readonly Action<bool, object> _publish;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private OwnedPetMapWindow? _window;
    private bool _requested, _disposed;
    private int _region;
    private nint _game;
    public OwnPetMapView(PetProgress progress, UserSettings settings, Func<int> map, Func<PlayerMapPosition?> position, Action<bool, object> publish)
    {
        _progress = progress; _settings = settings; _map = map; _position = position; _publish = publish;
        _timer.Tick += (_, _) => Refresh(); _timer.Start(); Refresh();
    }
    public void Arm()
    {
        if (_disposed || !_settings.ShowPetMap) return;
        if (_window?.IsVisible == true) { Stop(); return; }
        _requested = true;
        try
        {
            if (_window == null)
            {
                _window = new OwnedPetMapWindow(_progress, _settings, _position); _window.HideRequested += Stop;
                PlaceWindow();
            }
            _window.Open(_map());
        }
        catch (Exception error) { WriteError(error); _requested = false; }
        Refresh();
    }
    private void PlaceWindow()
    {
        if (_window == null) return;
        if (_game == 0)
        {
            foreach (var process in Process.GetProcessesByName("AION2"))
                using (process) if (process.MainWindowHandle != 0) { _game = process.MainWindowHandle; break; }
        }
        if (_game != 0 && Native.GetClientRect(_game, out var bounds))
        {
            var origin = new Native.Point(); Native.ClientToScreen(_game, ref origin);
            var dpi = Math.Max(96u, Native.GetDpiForWindow(_game)) / 96d;
            _window.Width = Math.Max(900, Math.Min(1600, bounds.Right * .94 / dpi));
            _window.Height = Math.Max(580, Math.Min(920, bounds.Bottom * .90 / dpi));
            _window.Left = origin.X / dpi + (bounds.Right / dpi - _window.Width) / 2;
            _window.Top = origin.Y / dpi + (bounds.Bottom / dpi - _window.Height) / 2;
        }
        else
        {
            var area = SystemParameters.WorkArea; _window.Width = Math.Min(1400, area.Width * .94); _window.Height = Math.Min(860, area.Height * .9);
            _window.Left = area.Left + (area.Width - _window.Width) / 2; _window.Top = area.Top + (area.Height - _window.Height) / 2;
        }
    }
    public void Stop() { _requested = false; _window?.Hide(); Refresh(); }
    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            if (!_settings.ShowPetMap) { _requested = false; _window?.Hide(); }
            var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var pid);
            var own = pid == Environment.ProcessId; var game = false;
            if (pid != 0 && !own)
                try { using var process = Process.GetProcessById((int)pid); game = process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase); if (game) _game = foreground; } catch (Exception) { }
            if (_requested && _window != null)
            {
                if (!own && !game) _window.Hide();
                else if (!_window.IsVisible) _window.Show();
            }
            var region = _map();
            if (_region != 0 && region != 0 && _region != region && _window?.IsVisible == true) _window.Open(region);
            if (region != 0) _region = region;
            _publish(_window?.IsVisible == true, new { Mode = "OwnedMap", Armed = _requested, Visible = _window?.IsVisible == true,
                Ready = true, ScreenCapture = false, SharedScene = true, Player = _position(), Map = _window?.DiagnosticState });
        }
        catch (Exception error) { WriteError(error); }
    }
    private static void WriteError(Exception error)
    {
        try { Directory.CreateDirectory(UserSettings.DataDirectory); File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "owned-map-error.txt"), error.ToString()); } catch (IOException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _window?.DisposeWindow(); }
}
