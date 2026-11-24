using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AionMeter.Core.Bosses;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum;

internal sealed class BossAssistantController : IDisposable
{
    private readonly object _gate = new();
    private readonly UserSettings _settings;
    private readonly GameData _data;
    private readonly CombatTracker _combat;
    private readonly BossOverlayWindow _overlay;
    private readonly BossOverlayWindow _warning;
    private readonly BossJournal _journal;
    private readonly BossSound _sound = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private BossAssistantEngine _live = null!;
    private BossLiveAdapter _adapter = null!;
    private BossAssistantEngine? _playback;
    private BossReplay? _replay;
    private readonly Stopwatch _replayClock = new();
    private BossSettingsWindow? _window;
    private BossSignal? _lastCast;
    private bool _disposed;
    private IReadOnlyList<BossTimingProposal> _proposals = [];
    internal BossProfileCatalog Catalog { get; private set; } = new([]);
    internal string Mode { get; private set; } = "LIVE";
    internal string Message { get; private set; } = "Gotowy · oczekuję na walkę z bossem";
    internal string ProfileDirectory => Path.Combine(UserSettings.DataDirectory, "bossmods");
    internal string JournalDirectory => _journal.DirectoryPath;
    internal BossAssistantSnapshot State { get; private set; } = new([], [], [], 0);
    internal object DiagnosticState => new { Enabled = _settings.BossAssistantEnabled, Mode, Message, Profiles = Catalog.Profiles.Count(p => !p.Synthetic),
        ProfileErrors = Catalog.Errors, Fights = State.Fights.Count, Bars = State.Bars.Count, JournalDropped = _journal.Dropped, AudioError = _sound.LastError, Proposals = _proposals.Count };
    internal BossAssistantController(UserSettings settings, GameData data, CombatTracker combat, int map)
    {
        _settings = settings; _data = data; _combat = combat;
        _settings.DisabledBossMechanics ??= []; _settings.BossWarningSeconds ??= [10, 5, 3];
        _overlay = new(settings); _warning = new(settings, warning: true); _journal = new(Path.Combine(UserSettings.DataDirectory, "boss-fights"));
        LoadProfiles(map); _timer.Tick += (_, _) => Refresh(); _timer.Start();
        if (_settings.BossLearnedTimers) _ = RestoreLearning();
    }
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private void LoadProfiles(int map)
    {
        lock (_gate) {
            _live?.Reset(Now, "Profiles reloaded");
            Catalog = BossProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "bossmods"), ProfileDirectory);
            _live = new(Catalog); _live.Record += record => {
                if (_settings.BossJournal) _journal.Add(record);
                if (record.Signal.Kind == BossSignalKind.Cast) _lastCast = record.Signal;
            };
            _adapter = new(_data, _live, _combat.IsCurrentGroupActor, () => _combat.ActiveBoss, map);
            ApplyOptions();
        }
    }
    internal void ReloadProfiles() { Live(); LoadProfiles(_combat.ExportCache().MapId); Message = Catalog.Errors.Count == 0 ? "Profile wczytane ponownie" : string.Join("\n", Catalog.Errors); }
    internal void Process(GameEvent ev) { lock (_gate) { if (!_disposed) _adapter.Process(ev); } }
    internal void ConnectionLost() { lock (_gate) { _adapter.Clear(Now, "Capture connection lost"); _sound.Clear(); } }
    internal void ApplyOptions()
    {
        lock (_gate) {
            var options = new BossEngineOptions { Role = _settings.BossRole, CommunityTimers = _settings.BossCommunityTimers,
                LearnedTimers = _settings.BossLearnedTimers, WarningSeconds = _settings.BossWarningSeconds.Where(s => double.IsFinite(s) && s > 0 && s <= 60).Take(8).ToArray(),
                DisabledMechanics = _settings.DisabledBossMechanics.ToHashSet(StringComparer.Ordinal) };
            _adapter.Enabled = _settings.BossAssistantEnabled;
            _live.Configure(options); _live.SetProposals(_proposals); _playback?.Configure(options);
            if (!_settings.BossAssistantEnabled) { _live.Reset(Now, "Disabled"); _sound.Clear(); }
        }
        _overlay.ApplyOptions(); _warning.ApplyOptions();
    }
    internal void SaveOptions() { _settings.Save(); ApplyOptions(); Refresh(); }
    internal void OpenSettings()
    {
        if (_window == null) { _window = new(this, _settings); _window.Closed += (_, _) => { _window = null; Refresh(); }; }
        _window.Show(); _window.Activate(); Refresh();
    }
    internal void Live()
    {
        _sound.Clear(); _replayClock.Reset(); _replay = null; _playback = null; Mode = "LIVE";
        Message = "Tryb gry · oczekuję na zdarzenia bossa"; Refresh();
    }
    internal void Demo()
    {
        Mode = "DEMO"; _playback = new(Catalog, true); ApplyOptions();
        _replay = new(_playback, DemoSignals()); _replayClock.Restart();
        Message = "DEMO · fikcyjny boss i czasy. Po teście wybierz Powrót do gry."; Refresh();
    }
    internal void Replay(string file)
    {
        var records = BossJournal.Read(file); if (records.Count == 0) throw new InvalidDataException("Dziennik nie zawiera zdarzeń");
        Mode = "REPLAY"; _playback = new(Catalog, true); ApplyOptions();
        _replay = new(_playback, records.Select(r => r.Signal)); _replayClock.Restart();
        Message = "Odtwarzanie: " + Path.GetFileName(file); Refresh();
    }
    internal async Task<string> Analyze()
    {
        Message = "Analizuję lokalne walki…";
        var result = await Task.Run(() => {
            var rows = new List<BossLogRecord>(); var errors = 0;
            if (Directory.Exists(JournalDirectory)) foreach (var file in new DirectoryInfo(JournalDirectory).EnumerateFiles("boss-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(50)) {
                try { rows.AddRange(BossJournal.Read(file.FullName, allowIncompleteTail: true)); } catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { errors++; }
                if (rows.Count >= 250000) break;
            }
            var proposals = BossFightAnalyzer.Analyze(rows);
            Directory.CreateDirectory(JournalDirectory);
            File.WriteAllText(Path.Combine(JournalDirectory, "timing-proposals.json"), JsonSerializer.Serialize(proposals, new JsonSerializerOptions(BossJson.Options) { WriteIndented = true }));
            return (proposals, errors, rows.Count);
        });
        _proposals = result.proposals; ApplyOptions();
        Message = $"Analiza: {result.Count} zdarzeń, {_proposals.Count} propozycji, {_proposals.Count(p => p.Eligible)} stabilnych. Pominięte pliki: {result.errors}.";
        return Message;
    }
    private async Task RestoreLearning()
    {
        try { await Analyze(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message = "Nie można wczytać pomiarów: " + e.Message; }
    }
    internal void MarkLastCast()
    {
        lock (_gate) {
            if (_lastCast == null || Now - _lastCast.At > 10000) { Message = "Brak odczytanego skilla z ostatnich 10 sekund."; return; }
            _live.Process(_lastCast with { At = Now, Kind = BossSignalKind.Mark, Source = "User important mechanic" });
            Message = "Oznaczono: " + _lastCast.SkillName + " (#" + _lastCast.SkillId + ")";
        }
    }
    internal void TestSound() => _sound.Enqueue(new("test", "Test ostrzeżenia Aurum", "warning", Now, true, false, true), _settings.BossVolume);
    private void Refresh()
    {
        if (_disposed) return;
        var now = Now; BossAssistantEngine active; long time;
        lock (_gate) {
            _live.Tick(now);
            if (_replay != null && _playback != null) {
                _replay.AdvanceTo(_replay.StartedAt + _replayClock.ElapsedMilliseconds);
                active = _playback; time = _replay.Now; _live.TakeAlerts();
                if (_replay.Complete) Message = Mode + " zakończone · wybierz Powrót do gry";
            } else { active = _live; time = now; }
            State = active.Snapshot(time);
            var alerts = active.TakeAlerts();
            if (_settings.BossAssistantEnabled && _settings.BossSounds && GameWindowAccess.GameOrAurumForeground())
                foreach (var alert in alerts.Where(a => a.Sound && time - a.At <= 1000).TakeLast(1)) _sound.Enqueue(alert, _settings.BossVolume);
        }
        var placement = _window?.IsVisible == true && !_settings.BossOverlayLocked;
        var foreground = GameWindowAccess.GameOrAurumForeground();
        var barsVisible = (_settings.BossAssistantEnabled && State.Bars.Count > 0 || placement) && foreground;
        var warningVisible = (_settings.BossAssistantEnabled && State.Alerts.Count > 0 || placement) && foreground;
        if (barsVisible) { _overlay.Update(State, Mode, placement); if (!_overlay.IsVisible) _overlay.Show(); }
        else if (_overlay.IsVisible) _overlay.Hide();
        if (warningVisible) { _warning.Update(State, Mode, placement); if (!_warning.IsVisible) _warning.Show(); }
        else if (_warning.IsVisible) _warning.Hide();
    }
    internal static BossSignal[] DemoSignals()
    {
        BossSignal S(long at, BossSignalKind kind, int skill = 0, double? hp = null) => new(at, kind, 9001, -9001)
            { BossName = "Strażnik Aurum · TEST", SkillId = skill, SkillName = "TEST", HpPercent = hp, Synthetic = true, PullConfirmed = true, Source = "Synthetic demo" };
        return [S(1000, BossSignalKind.Pull, hp: 100), S(17000, BossSignalKind.Cast, -101), S(21000, BossSignalKind.Hit), S(29000, BossSignalKind.Cast, -102),
            S(33000, BossSignalKind.Hp, hp: 69), S(45000, BossSignalKind.Cast, -103), S(50000, BossSignalKind.Hit), S(61000, BossSignalKind.Death)];
    }
    internal static void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        var catalog = BossProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "bossmods"));
        var engine = new BossAssistantEngine(catalog, true); var replay = new BossReplay(engine, DemoSignals());
        var window = new BossOverlayWindow(new UserSettings());
        var warning = new BossOverlayWindow(new UserSettings(), warning: true);
        foreach (var time in new long[] { 1000, 12000, 18000, 35000, 44000, 62000 }) {
            replay.AdvanceTo(time); window.RenderPreview(engine.Snapshot(time), "DEMO", Path.Combine(directory, "boss-" + time + ".png"));
            warning.RenderPreview(engine.Snapshot(time), "DEMO", Path.Combine(directory, "warning-" + time + ".png"));
        }
        File.WriteAllText(Path.Combine(directory, "diagnostics.json"), JsonSerializer.Serialize(new { Profiles = catalog.Profiles.Count(p => !p.Synthetic),
            Mechanics = catalog.Profiles.Where(p => !p.Synthetic).Sum(p => p.Mechanics.Length), catalog.Errors, FightsAfterDeath = engine.Snapshot(62000).Fights.Count,
            BarsAfterDeath = engine.Snapshot(62000).Bars.Count })); window.Close(); warning.Close();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _live.Reset(Now, "Aurum closed"); }
        _timer.Stop(); _window?.Close(); _overlay.Close(); _warning.Close(); _sound.Dispose(); _journal.Dispose();
    }
}
