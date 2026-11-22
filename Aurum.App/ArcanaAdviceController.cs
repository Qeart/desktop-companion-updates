using System.IO;
using System.Windows.Threading;
using AionMeter.Core.Events;
using AionMeter.Core.Gear;

namespace Aurum;

internal sealed class ArcanaAdviceController : IDisposable
{
    private readonly UserSettings _settings;
    private readonly Func<GameClass> _class;
    private readonly Func<bool> _suppressed;
    private readonly ArcanaScreenReader _reader = new();
    private readonly ArcanaAdviceCatalog _catalog = ArcanaAdviceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "arcana-advice.json"));
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private ArcanaAdviceWindow? _window;
    private ArcanaPanel? _panel;
    private bool _busy, _disposed, _enabled;
    private int _generation;
    private long _lastPoll;
    internal bool HasView => _window?.IsVisible == true;
    internal object DiagnosticState => new { Enabled = _settings.ShowItemAdvice, Visible = HasView, Class = _class().ToString(),
        Profile = "PvE DPS", Card = _panel, Reader = _reader.DiagnosticState };
    internal ArcanaAdviceController(UserSettings settings, Func<GameClass> playerClass, Func<bool> suppressed)
    {
        _settings = settings; _class = playerClass; _suppressed = suppressed;
        _reader.Updated += () => _timer.Dispatcher.BeginInvoke(() => { if (!_disposed) { _lastPoll = 0; _ = Tick(); } });
        _timer.Tick += async (_, _) => await Tick(); _timer.Start();
    }
    internal void RefreshEnabled() { if (!_settings.ShowItemAdvice) { _generation++; _window?.Hide(); } else { _lastPoll = 0; _ = Tick(); } }
    private async Task Tick()
    {
        if (_disposed) return;
        var enabled = _settings.ShowItemAdvice && !_suppressed();
        if (_enabled != enabled) { _enabled = enabled; _generation++; }
        var game = enabled ? GameWindowAccess.ForegroundGame() : null;
        if (game == null) { _window?.Hide(); return; }
        if (_busy || Environment.TickCount64 - _lastPoll < 80) return;
        _lastPoll = Environment.TickCount64; _busy = true; var generation = _generation;
        try
        {
            var panel = await Task.Run(() => _reader.Poll(game.Value));
            if (_disposed || generation != _generation) return;
            if (!_settings.ShowItemAdvice || _suppressed() || Native.GetForegroundWindow() != game.Value.Handle) { _window?.Hide(); return; }
            _panel = panel;
            var advice = panel == null ? null : _catalog.For(_class(), panel);
            if (panel == null || advice == null) { _window?.Hide(); return; }
            _window ??= new ArcanaAdviceWindow(); _window.Present(panel, advice, game.Value);
        }
        catch (Exception error)
        {
            if (!_disposed) _window?.Hide();
            _ = Task.Run(() => { try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "arcana-advice-error.txt"), error.ToString()); } catch (IOException) { } });
        }
        finally { _busy = false; }
    }
    public void Dispose() { _disposed = true; _generation++; _timer.Stop(); _reader.Dispose(); _window?.Close(); }
}
