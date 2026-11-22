using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;
namespace Aurum;
internal sealed class PetMapView : IPetMapView
{
    private readonly PetProgress _progress;
    private readonly PetMapCatalog _catalog;
    private readonly UserSettings _settings;
    private readonly Task<PetMapMatcher> _matcher;
    private readonly Func<int> _currentMap;
    private readonly nint _meterWindow;
    private readonly Action<bool, object> _publish;
    private PetMapFramePump? _pump;
    private long _lastSequence;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
    private PetMapPicker? _picker;
    private PetMapRings? _rings;
    private PetMapAlignment? _alignment;
    private Native.Point _origin;
    private int _width, _height, _preferred, _region, _generation;
    private nint _gameWindow, _lastForeground;
    private bool _gameForeground, _wasM, _armed = true, _visible, _disposed, _suspended;
    private long _detectUntil;
    private long _lastStatusWrite;
    private int _areaPetId, _areaMapId, _frames;
    private IReadOnlyList<PetSpawnArea> _areas = [];
    private double _worstFrame;
    private int _newFrames;
    private readonly Queue<object> _history = new();
    private Task? _statusWrite;
    public bool HasMapView => _visible && _picker?.IsVisible == true;
    public object DiagnosticState => new { Armed = _armed, Visible = _visible, DedicatedRenderer = true, Ready = _matcher.IsCompletedSuccessfully,
        MapId = _alignment?.MapId, Inliers = _alignment?.Inliers, SelectedPet = _picker?.SelectedPet?.Name };
    public PetMapView(PetProgress progress, UserSettings settings, Func<int> currentMap, nint meterWindow, Action<bool, object> publish)
    {
        _progress = progress; _settings = settings; _currentMap = currentMap; _meterWindow = meterWindow; _publish = publish;
        _catalog = PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json"));
        _matcher = Task.Run(() => new PetMapMatcher(_catalog, Path.Combine(AppContext.BaseDirectory, "data", "pet-maps")));
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        CompositionTarget.Rendering += OnRendering;
    }
    private void OnRendering(object? sender, EventArgs args) { if (_visible) Tick(); }
    public void Arm() { _generation++; _alignment = null; _armed = true; _detectUntil = Environment.TickCount64 + 3000; }
    public void Stop() { _generation++; _alignment = null; _armed = false; _visible = false; _pump?.Pause(); Hide(); }
    private void Hide() { _picker?.Hide(); _rings?.Hide(); }
    private void Tick()
    {
        try { RefreshFrame(); }
        catch (Exception error) { Hide(); WriteError(error); }
        finally { _publish(HasMapView, DiagnosticState); }
    }
    private void RefreshFrame()
    {
        if (_disposed) return;
        var key = Native.GetAsyncKeyState(0x4D); var down = (key & 0x8000) != 0; var pressed = down && !_wasM || (key & 1) != 0; _wasM = down;
        var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var processId);
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            try { using var process = Process.GetProcessById((int)processId); _gameForeground = process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { _gameForeground = false; }
        }
        if (_gameForeground) _gameWindow = foreground;
        var pickerActive = processId == Environment.ProcessId && _picker?.IsVisible == true;
        if (!_settings.ShowPetMap) { Stop(); return; }
        if (pressed && _gameForeground) { if (_visible) Stop(); else Arm(); }
        if (!_gameForeground && !pickerActive) { Hide(); _pump?.Pause(); _suspended = true; return; }
        if (_suspended) { _suspended = false; if (_armed) Arm(); }
        var region = _currentMap();
        if (_region != 0 && region != 0 && _region != region) { _preferred = 0; if (_armed) Arm(); _rings?.Hide(); }
        if (region != 0) _region = region;
        if (!_armed) return;
        if (!_matcher.IsCompletedSuccessfully)
        {
            if (_matcher.IsFaulted)
            {
                WriteError(_matcher.Exception!); Stop();
            }
            return;
        }
        if (_gameWindow == 0 || !Native.GetClientRect(_gameWindow, out var bounds) || bounds.Right < 640 || bounds.Bottom < 480) return;
        var origin = new Native.Point(); if (!Native.ClientToScreen(_gameWindow, ref origin)) return;
        _pump ??= new PetMapFramePump(request => _matcher.Result.Read(request.X, request.Y, request.Width, request.Height,
            request.PreferredMap, request.Session, request.Exclusions));
        var preferred = _preferred != 0 ? _preferred : region;
        _pump.Request(new(origin.X, origin.Y, bounds.Right, bounds.Bottom, preferred, _generation, CaptureExclusions(origin)));
        var frame = _pump.Latest;
        if (frame == null || frame.Sequence == _lastSequence || frame.Request.Session != _generation) return;
        if (frame.Request.X != origin.X || frame.Request.Y != origin.Y || frame.Request.Width != bounds.Right || frame.Request.Height != bounds.Bottom) return;
        _lastSequence = frame.Sequence;
        if (frame.Error != null) { Hide(); WriteError(frame.Error); return; }
        var match = frame.Match!;
        var submitDelay = Stopwatch.GetElapsedTime(frame.CompletedAt).TotalMilliseconds;
        var total = Stopwatch.GetElapsedTime(frame.StartedAt).TotalMilliseconds;
        var work = Stopwatch.StartNew();
        try
        {
            var active = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(active, out var activeId);
            if (active != _gameWindow && activeId != Environment.ProcessId) { Hide(); return; }
            if (match.Alignment is not { } alignment)
            {
                _alignment = null; _rings?.Hide();
                PublishStatus(match, total + work.Elapsed.TotalMilliseconds, 0, submitDelay);
                if (match.Diagnostics?.FailedCalibrations >= 2 && Environment.TickCount64 >= _detectUntil) Stop();
                return;
            }
            _visible = true; _preferred = alignment.MapId; _alignment = alignment;
            _detectUntil = Environment.TickCount64 + 3000;
            _origin = new Native.Point { X = frame.Request.X, Y = frame.Request.Y }; _width = frame.Request.Width; _height = frame.Request.Height;
            if (_picker == null)
            {
                _picker = new PetMapPicker(_progress, _catalog, _settings);
                _picker.SelectionChanged += Redraw; _picker.HideRequested += Stop;
            }
            _picker.SetContext(alignment.MapId);
            var dpi = VisualTreeHelper.GetDpi(_picker);
            _picker.Left = (origin.X + bounds.Right - 310) / dpi.DpiScaleX; _picker.Top = (origin.Y + bounds.Bottom * .12) / dpi.DpiScaleY;
            if (!_picker.IsVisible) _picker.Show();
            var renderStart = work.Elapsed.TotalMilliseconds;
            Redraw();
            var renderMs = work.Elapsed.TotalMilliseconds - renderStart;
            PublishStatus(match, total + work.Elapsed.TotalMilliseconds, renderMs, submitDelay);
        }
        catch (Exception error) { Hide(); WriteError(error); }
    }
    private OpenCvSharp.Rect[] CaptureExclusions(Native.Point origin)
    {
        var result = new List<OpenCvSharp.Rect>();
        if (Native.GetCursorPos(out var cursor)) result.Add(new(cursor.X - origin.X - 24, cursor.Y - origin.Y - 24, 48, 48));
        // Mask other Aurum windows if the player has placed the meter over the map.
        if (_meterWindow != 0 && Native.IsWindowVisible(_meterWindow) && Native.GetWindowRect(_meterWindow, out var meter))
        {
            result.Add(new(meter.Left - origin.X - 8, meter.Top - origin.Y - 8, meter.Right - meter.Left + 16, meter.Bottom - meter.Top + 16));
        }
        return result.ToArray();
    }
    private void PublishStatus(PetMapMatch match, double total, double render, double submitDelay)
    {
        var now = Environment.TickCount64;
        if (_lastStatusWrite == 0) _lastStatusWrite = now;
        _frames++; if (match.NewFrame) _newFrames++;
        _worstFrame = Math.Max(_worstFrame, total);
        _history.Enqueue(new { At = DateTimeOffset.UtcNow, match.NewFrame, match.TotalMilliseconds, EndToEndMilliseconds = total,
            SourceFrameAgeMilliseconds = match.SourceFrameAgeMilliseconds + submitDelay, SubmitDelayMilliseconds = submitDelay, match.Alignment, match.Diagnostics });
        while (_history.Count > 180) _history.Dequeue();
        if (now - _lastStatusWrite < 1000 || _statusWrite is { IsCompleted: false }) return;
        var interval = Math.Max(1, now - _lastStatusWrite);
        var status = new {
            UpdatedAt = DateTimeOffset.UtcNow, match.Milliseconds, match.CaptureMilliseconds, match.TotalMilliseconds, match.CaptureBackend, match.CaptureError, match.NewFrame,
            RenderMilliseconds = render, EndToEndMilliseconds = total, SubmitDelayMilliseconds = submitDelay,
            SourceFrameAgeMilliseconds = match.SourceFrameAgeMilliseconds + submitDelay, FramesPerSecond = _frames * 1000d / interval,
            NewFramesPerSecond = _newFrames * 1000d / interval, WorstFrameMilliseconds = _worstFrame,
            match.FrameFeatures, match.Tracked, match.Alignment, match.Diagnostics,
            OverlayExcludedFromCapture = _rings?.ExcludedFromCapture, Pet = _picker?.SelectedPet?.Name, Areas = _areas.Count
        };
        var history = _history.ToArray();
        _lastStatusWrite = now; _frames = _newFrames = 0; _worstFrame = 0;
        _statusWrite = Task.Run(() => {
            try {
                File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "pet-map-status.json"), System.Text.Json.JsonSerializer.Serialize(status));
                File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "pet-map-frames.json"), System.Text.Json.JsonSerializer.Serialize(history));
            } catch (IOException) { }
        });
    }
    private void Redraw()
    {
        if (_disposed || !_visible || _alignment is not { } alignment || _picker?.SelectedPet is not { } pet) { _rings?.Hide(); return; }
        var map = _catalog.Map(alignment.MapId); if (map == null) return;
        if (_areaPetId != pet.Id || _areaMapId != map.Id)
        {
            _areaPetId = pet.Id; _areaMapId = map.Id; _areas = _catalog.Areas(map.Id, pet);
        }
        _rings ??= new PetMapRings(); _rings.Draw(map, alignment, _areas, _origin, _width, _height);
    }
    private static void WriteError(Exception error)
    {
        try { Directory.CreateDirectory(UserSettings.DataDirectory); File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "pet-map-error.txt"), error.ToString()); } catch (IOException) { }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop(); CompositionTarget.Rendering -= OnRendering; Stop(); _picker?.Close(); _rings?.Close();
        _ = Task.Run(async () => { try { _pump?.Dispose(); (await _matcher).Dispose(); } catch (Exception) { } });
    }
}
