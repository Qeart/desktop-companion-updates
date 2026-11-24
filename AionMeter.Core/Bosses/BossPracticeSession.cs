namespace AionMeter.Core.Bosses;

public sealed record BossPracticeStatus(bool Running, long StartedAt, long EndsAt, long StoppedAt, int Events, int Casts,
    int Skills, int Fights, int Markers, int Dropped, int LastNpcCode);
public sealed record BossPracticeSkill(int NpcCode, int SkillId, string Name, int Casts, int HitSamples);
public sealed record BossPracticeCandidate(int SkillId, string Name, BossSignalKind Kind, double SecondsFromMarker);
public sealed record BossPracticeMarker(long At, Guid SessionId, int NpcCode, IReadOnlyList<BossPracticeCandidate> Candidates);
public sealed record BossPracticeNpc(int NpcCode, string Name, bool HasTimerProfile, int Events);
public sealed record BossPracticeReport(int SchemaVersion, string Target, long StartedAt, long StoppedAt, int Dropped,
    IReadOnlyList<BossPracticeNpc> Npcs, IReadOnlyList<BossPracticeSkill> Skills, IReadOnlyList<BossPracticeMarker> Markers,
    IReadOnlyList<BossTimingProposal> TimingProposals, IReadOnlyList<BossLogRecord> Timeline);

/// <summary>Explicit, bounded observation of Krao Cave's final boss. Does not create timers or change NPC classification.</summary>
public sealed class BossPracticeSession
{
    // All four client forms, in each of the three difficulties. Identity comes from
    // data/npcs/en.json (Ultimate Berk, Krao Cave). This list is NOT a timer alias map.
    public static bool IsBerk(int npc) => npc is >= 2300171 and <= 2300174 or >= 2310171 and <= 2310174 or >= 2320171 and <= 2320174;
    private readonly object _gate = new();
    private readonly List<BossLogRecord> _records = new();
    private readonly Dictionary<Guid, BossLogRecord> _active = new();
    private readonly int _limit;
    private long _started, _ends, _stopped;
    private bool _running;
    private int _dropped;
    public BossPracticeSession(int limit = 20000) => _limit = Math.Clamp(limit, 8, 20000);
    public bool IsRunning { get { lock (_gate) return _running; } }
    public void Start(long at)
    {
        if (at < 0) throw new ArgumentOutOfRangeException(nameof(at));
        lock (_gate) { _started = at; _ends = at + 15 * 60_000; _stopped = 0; _running = true; _dropped = 0; _records.Clear(); _active.Clear(); }
    }
    public void Stop(long at) { lock (_gate) { if (!_running) return; _stopped = Math.Clamp(at, _started, _ends); _running = false; } }
    public bool Tick(long now) { lock (_gate) { if (!_running || now < _ends) return false; Stop(_ends); return true; } }
    public bool Observe(BossLogRecord record)
    {
        var s = record.Signal;
        lock (_gate) {
            if (!_running || record.SchemaVersion != 1 || record.SessionId == Guid.Empty || s.Synthetic || !IsBerk(s.NpcCode) ||
                s.Actor == 0 || s.At < _started || s.At >= _ends || s.SkillId < 0) return false;
            if (_records.Count >= _limit) { _dropped++; return true; }
            _records.Add(record);
            if (s.Kind is BossSignalKind.Reset or BossSignalKind.Death) _active.Remove(record.SessionId);
            else if (s.Kind != BossSignalKind.Mark) _active[record.SessionId] = record;
            return true;
        }
    }
    public BossLogRecord? Mark(long at)
    {
        lock (_gate) {
            if (!_running || at < _started || at >= _ends) return null;
            var recent = _active.Values.Where(r => at >= r.Signal.At && at - r.Signal.At <= 10000).OrderByDescending(r => r.Signal.At).FirstOrDefault();
            if (recent == null || _records.Count >= _limit) return null;
            if (_records.LastOrDefault(r => r.Signal.Kind == BossSignalKind.Mark) is { } previous && at - previous.Signal.At < 500) return null;
            // A human timestamp does not prove which skill caused the visible mechanic.
            var mark = recent with { Signal = recent.Signal with { At = at, Kind = BossSignalKind.Mark, SkillId = 0, SkillName = "",
                HpPercent = null, Source = "User visual marker; mechanic/skill association unverified" } };
            _records.Add(mark); return mark;
        }
    }
    public BossPracticeStatus Status
    {
        get { lock (_gate) return new(_running, _started, _ends, _stopped, _records.Count, _records.Count(r => r.Signal.Kind == BossSignalKind.Cast),
            _records.Where(r => r.Signal.Kind == BossSignalKind.Cast).Select(r => r.Signal.SkillId).Distinct().Count(),
            _records.Select(r => r.SessionId).Distinct().Count(), _records.Count(r => r.Signal.Kind == BossSignalKind.Mark), _dropped, _records.LastOrDefault()?.Signal.NpcCode ?? 0); }
    }
    public BossPracticeReport Report(BossProfileCatalog catalog, long at)
    {
        BossLogRecord[] rows; long started, stopped; int dropped;
        lock (_gate) { rows = _records.ToArray(); started = _started; stopped = _stopped > 0 ? _stopped : at; dropped = _dropped; }
            rows = rows.OrderBy(r => r.Signal.At).ToArray();
            var npcs = rows.GroupBy(r => r.Signal.NpcCode).Select(g => new BossPracticeNpc(g.Key, g.First().Signal.BossName,
                catalog.Find(g.Key, g.First().Signal.MapId, false) != null, g.Count())).ToArray();
            var skills = rows.Where(r => r.Signal.SkillId > 0 && r.Signal.Kind is BossSignalKind.Cast or BossSignalKind.Hit)
                .GroupBy(r => (r.Signal.NpcCode, r.Signal.SkillId)).Select(g => new BossPracticeSkill(g.Key.NpcCode, g.Key.SkillId,
                    g.First().Signal.SkillName, g.Count(r => r.Signal.Kind == BossSignalKind.Cast), g.Count(r => r.Signal.Kind == BossSignalKind.Hit))).ToArray();
            var markers = rows.Where(r => r.Signal.Kind == BossSignalKind.Mark).Select(mark => new BossPracticeMarker(mark.Signal.At, mark.SessionId, mark.Signal.NpcCode,
                rows.Where(r => r.SessionId == mark.SessionId && r.Signal.Actor == mark.Signal.Actor && r.Signal.SkillId > 0 &&
                    r.Signal.Kind is BossSignalKind.Cast or BossSignalKind.Hit && r.Signal.At - mark.Signal.At is >= -3000 and <= 2000)
                .OrderBy(r => Math.Abs(r.Signal.At - mark.Signal.At)).Take(12)
                .Select(r => new BossPracticeCandidate(r.Signal.SkillId, r.Signal.SkillName, r.Signal.Kind, (r.Signal.At - mark.Signal.At) / 1000d)).ToArray())).ToArray();
            return new(1, "Krao Cave / Ultimate Berk", started, stopped, dropped,
                npcs, skills, markers, BossFightAnalyzer.Analyze(rows), rows);
    }
}
