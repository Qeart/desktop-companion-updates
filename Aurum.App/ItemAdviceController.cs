using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AionMeter.Core.Events;
using AionMeter.Core.Gear;

namespace Aurum;

internal sealed class ItemAdviceController : IDisposable
{
    private readonly UserSettings _settings;
    private readonly Func<GameClass> _class;
    private readonly Func<bool> _suppressed;
    private readonly GearAdviceCatalog _catalog = GearAdviceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "gear-advice.json"));
    private readonly ItemTooltipReader _reader = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ItemAdviceWindow? _window;
    private bool _busy, _armed, _disposed;
    private Native.Point? _cursor;
    private int _generation;
    private long _stableAt, _lastRead, _readMs;
    private string _mode = "Idle", _status = "Najedź na przedmiot i przytrzymaj Ctrl";
    private ItemTooltipReading? _reading;
    internal object DiagnosticState => new { Enabled = _settings.ShowItemAdvice, Visible = _window?.IsVisible == true,
        Class = _class().ToString(), Profile = "PvE DPS", Trigger = "Ctrl", Armed = _armed, Busy = _busy, Status = _status,
        Milliseconds = _readMs, Mode = _mode, Item = _reading?.Item, Reader = _reader.DiagnosticState };
    internal ItemAdviceController(UserSettings settings, Func<GameClass> playerClass, Func<bool> suppressed)
    {
        _settings = settings; _class = playerClass; _suppressed = suppressed;
        _timer.Tick += async (_, _) => await Tick(); _timer.Start();
    }
    private static bool CtrlOnly() => (Native.GetAsyncKeyState(0x11) & 0x8000) != 0
        && (Native.GetAsyncKeyState(0x10) & 0x8000) == 0 && (Native.GetAsyncKeyState(0x12) & 0x8000) == 0;
    internal void RefreshEnabled() { if (!_settings.ShowItemAdvice) Cancel(); else _ = Tick(); }
    private void Cancel()
    {
        if (_armed) _generation++;
        if (_armed) _reader.Invalidate();
        _armed = false; _cursor = null; _lastRead = 0; _window?.Hide();
    }
    private async Task Tick()
    {
        if (_disposed) return;
        if (!_settings.ShowItemAdvice || !CtrlOnly() || _suppressed()) { Cancel(); return; }
        var game = GameWindowAccess.ForegroundGame();
        if (game == null || !Native.GetCursorPos(out var point) || point.X < game.Value.X || point.Y < game.Value.Y
            || point.X >= game.Value.X + game.Value.Width || point.Y >= game.Value.Y + game.Value.Height) { Cancel(); return; }
        var now = Environment.TickCount64;
        if (!_armed) { _armed = true; _generation++; _reader.Invalidate(); _cursor = point; _stableAt = now; _lastRead = 0; }
        if (_cursor is not { } old || Math.Abs(point.X - old.X) > 4 || Math.Abs(point.Y - old.Y) > 4)
        { _cursor = point; _stableAt = now; _generation++; _reader.Invalidate(); _lastRead = 0; _window?.Hide(); }
        if (_busy || now - _stableAt < 100 || now - _lastRead < 150) return;
        var playerClass = _class();
        if (playerClass == GameClass.Unknown) { _status = "Czekam na klasę własnej postaci"; _window?.Hide(); return; }
        if (!_reader.Available) { _status = "Windows OCR jest niedostępny"; return; }
        _busy = true; _lastRead = now; var generation = _generation;
        try
        {
            var clock = Stopwatch.StartNew();
            var readsBefore = _reader.OcrReads;
            var reading = await Task.Run(() => _reader.Read(game.Value, point.X - game.Value.X, point.Y - game.Value.Y));
            _readMs = clock.ElapsedMilliseconds;
            if (_disposed || generation != _generation || !_settings.ShowItemAdvice || !CtrlOnly()
                || Native.GetForegroundWindow() != game.Value.Handle || _class() != playerClass || _suppressed()) return;
            if (!Native.GetCursorPos(out var current) || Math.Abs(current.X - point.X) > 5 || Math.Abs(current.Y - point.Y) > 5) { _window?.Hide(); return; }
            _reading = reading;
            if (reading == null)
            {
                _window?.Hide(); _mode = "NoTooltip"; _status = "Czekam na tooltip wyposażenia";
                _lastRead = Environment.TickCount64 + (_reader.OcrReads > readsBefore ? 400 : 0); return;
            }
            var advice = _catalog.For(playerClass, reading.Item);
            if (advice == null) { _window?.Hide(); _status = "Brak zweryfikowanego profilu dla tej klasy lub slotu"; return; }
            _mode = reading.Mode; _status = advice.ClassLabel + " · " + advice.SlotLabel;
            _window ??= new ItemAdviceWindow(); _window.Present(advice, reading.Bounds, game.Value, reading.ComparisonRight);
        }
        catch (Exception error)
        {
            if (_disposed) return;
            _window?.Hide(); _status = "Nie udało się odczytać tooltipa";
            _ = Task.Run(() => { try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "item-advice-error.txt"), error.ToString()); } catch (IOException) { } });
        }
        finally { _busy = false; }
    }
    public void Dispose() { _disposed = true; _generation++; _timer.Stop(); _window?.Close(); }
}
