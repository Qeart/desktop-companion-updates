namespace AionMeter.Core.Bosses;

public sealed class BossAssistantEngine
{
    private sealed class Fight
    {
        internal Guid Id = Guid.NewGuid();
        internal required BossSignal Pull;
        internal BossProfile? Profile;
        internal int Phase;
        internal double? Hp;
        internal long LastActivity, HpAt;
        internal long JournalHpAt = long.MinValue / 2;
        internal Dictionary<int, long> JournalHits = new();
        internal Dictionary<int, long> Casts = new();
        internal HashSet<string> HpRules = new();
    }
    private sealed class Bar
    {
        internal required string Key, Label, Reaction, Category, Kind, Evidence;
        internal required Fight Fight;
        internal required int Skill, Samples;
        internal required long Started, Due;
        internal required double Confidence;
        internal required double[] Warnings;
        internal HashSet<double> Warned = new();
        internal double Previous = double.PositiveInfinity;
    }
    private readonly object _gate = new();
    private readonly BossProfileCatalog _catalog;
    private readonly bool _allowSynthetic;
    private readonly Dictionary<uint, Fight> _fights = new();
    private readonly Dictionary<string, Bar> _bars = new();
    private readonly List<BossAlert> _recent = new();
    private readonly List<BossObservedSkill> _observed = new();
    private readonly Queue<BossAlert> _pending = new();
    private BossEngineOptions _options = new();
    private IReadOnlyList<BossTimingProposal> _proposals = [];
    public event Action<BossLogRecord>? Record;
    public BossAssistantEngine(BossProfileCatalog catalog, bool allowSynthetic = false) { _catalog = catalog; _allowSynthetic = allowSynthetic; }
    public bool HasFight(uint actor) { lock (_gate) return _fights.ContainsKey(actor); }
    public void Configure(BossEngineOptions options)
    {
        lock (_gate)
        {
            _options = options;
            _pending.Clear(); _recent.RemoveAll(a => a.Predicted);
            if (!options.ShowObservedSkills) _observed.Clear();
            foreach (var key in _bars.Where(p => p.Value.Kind == "learned" && !options.LearnedTimers ||
                p.Value.Fight.Profile is { Evidence: "Community" } && !options.CommunityTimers || options.DisabledMechanics.Contains(p.Key[(p.Key.LastIndexOf('/') + 1)..]) ||
                p.Value.Fight.Profile?.Mechanics.FirstOrDefault(m => MechanicKey(p.Value.Fight.Profile, m) == p.Key[(p.Key.LastIndexOf('/') + 1)..]) is { } rule && !Enabled(p.Value.Fight, rule)).Select(p => p.Key).ToArray()) _bars.Remove(key);
        }
    }
    public void SetProposals(IReadOnlyList<BossTimingProposal> proposals) { lock (_gate) _proposals = proposals.Where(p => p.Eligible).Take(512).ToArray(); }
    public static string MechanicKey(BossProfile? profile, BossMechanic m) => (profile?.Id ?? "observed") + "." + m.Id;
    private bool Enabled(Fight fight, BossMechanic m) => !_options.DisabledMechanics.Contains(MechanicKey(fight.Profile, m))
        && (m.Roles.Contains("all") || m.Roles.Contains(_options.Role)) && (m.Phase == null || m.Phase == fight.Phase)
        && (fight.Profile?.Evidence != "Community" || _options.CommunityTimers);
    public void Process(BossSignal signal)
    {
        if (signal.At < 0 || signal.Actor == 0 || signal.NpcCode == 0 || signal.BossName.Length > 160 || signal.SkillName.Length > 160 ||
            !double.IsFinite(signal.Confidence) || signal.Confidence is < 0 or > 1 ||
            (!_allowSynthetic && (signal.Synthetic || signal.NpcCode < 0 || signal.SkillId < 0)) ||
            signal.HpPercent is { } hp && (!double.IsFinite(hp) || hp is < 0 or > 100)) return;
        lock (_gate)
        {
            if (signal.Kind == BossSignalKind.Pull)
            {
                if (_fights.ContainsKey(signal.Actor) || _fights.Count >= 8) return;
                var profile = _catalog.Find(signal.NpcCode, signal.MapId, signal.Synthetic);
                var fight = new Fight { Pull = signal, Profile = profile, LastActivity = signal.At,
                    Phase = profile?.Mechanics.Any(m => m.Phase != null || m.SetPhase != null) == true ? 1 : 0, Hp = signal.HpPercent, HpAt = signal.At };
                _fights.Add(signal.Actor, fight); Log(fight, signal);
                if (signal.PullConfirmed) foreach (var rule in profile?.Mechanics ?? [])
                    if (rule.FirstSeconds is { } delay && Enabled(fight, rule)) Schedule(fight, rule, signal.At, delay);
                return;
            }
            if (!_fights.TryGetValue(signal.Actor, out var f) || f.Pull.NpcCode != signal.NpcCode || signal.At < f.Pull.At) return;
            if (signal.Kind is BossSignalKind.Death or BossSignalKind.Reset)
            { if (signal.At >= f.LastActivity) End(f, signal); return; }
            if (signal.Kind is BossSignalKind.Cast or BossSignalKind.Hit) f.LastActivity = Math.Max(f.LastActivity, signal.At);
            if (signal.Kind == BossSignalKind.Cast)
            {
                if (signal.SkillId == 0 || f.Casts.TryGetValue(signal.SkillId, out var previous) && signal.At - previous < 700) return;
                f.Casts[signal.SkillId] = signal.At;
                var rules = f.Profile?.Mechanics ?? [];
                foreach (var key in _bars.Where(p => p.Value.Fight == f && p.Value.Skill == signal.SkillId && p.Value.Kind != "cast").Select(p => p.Key).ToArray()) _bars.Remove(key);
                var own = rules.Where(m => m.SkillId == signal.SkillId).ToArray();
                var rule = own.FirstOrDefault(m => Enabled(f, m));
                if (_options.ShowObservedSkills && f.Profile?.QuietSkills.Contains(signal.SkillId) != true) {
                    var name = own.FirstOrDefault()?.Name ?? (string.IsNullOrWhiteSpace(signal.SkillName) ? "Atak #" + signal.SkillId : signal.SkillName);
                    var previousName = _observed.LastOrDefault(s => s.Actor == signal.Actor && s.Name == name);
                    if (previousName == null || signal.At - previousName.At >= 700) {
                        _observed.RemoveAll(s => s.Actor == signal.Actor && s.Name == name);
                        _observed.Add(new(f.Id + "/observed/" + signal.SkillId, signal.Actor, signal.SkillId, name, f.Pull.BossName, signal.At, signal.Synthetic));
                        if (_observed.Count > 4) _observed.RemoveAt(0);
                    }
                }
                if (rule is { AlertOnCast: true })
                    Alert(f, "cast/" + signal.SkillId + "/" + signal.At, rule.Reaction.Length > 0 ? rule.Reaction : rule.Name, rule.Category, signal.At, true, false, rule.AlertSeconds);
                foreach (var m in rules.Where(m => Enabled(f, m)))
                {
                    if (m.SkillId == signal.SkillId)
                    {
                        if (m.CastSeconds is { } duration) Schedule(f, m, signal.At, duration, "cast");
                        if (m.CooldownSeconds is { } cooldown) Schedule(f, m, signal.At, cooldown);
                    }
                    if (m.AfterSkillId == signal.SkillId && m.AfterSeconds is { } after) {
                        if (m.AlertOnAnchor) Alert(f, "anchor/" + m.Id + "/" + signal.At,
                            (m.Reaction.Length > 0 ? m.Reaction : m.Name) + $" · ≈{after:0.#} s", m.Category, signal.At, m.Confidence >= .8, true, m.AlertSeconds);
                        Schedule(f, m, signal.At, after, m.WindupFromAnchor ? "windup" : null);
                    }
                }
                if (_options.LearnedTimers && !rules.Any(m => m.SkillId == signal.SkillId && Enabled(f, m)))
                {
                    var proposal = _proposals.FirstOrDefault(p => p.NpcCode == signal.NpcCode && p.SkillId == signal.SkillId && p.Phase == f.Phase);
                    if (proposal != null) Schedule(f, new() { Id = "learned-" + signal.SkillId, SkillId = signal.SkillId, Name = signal.SkillName,
                        Confidence = proposal.Confidence, Samples = proposal.Samples, Evidence = "Propozycja z lokalnych obserwacji" }, signal.At, proposal.MedianSeconds, "learned");
                }
            }
            else if (signal.Kind == BossSignalKind.CastEnd)
            {
                foreach (var key in _bars.Where(p => p.Value.Fight == f && p.Value.Skill == signal.SkillId && p.Value.Kind == "cast").Select(p => p.Key).ToArray()) _bars.Remove(key);
            }
            else if (signal.Kind == BossSignalKind.Hp && signal.HpPercent is { } percent && signal.At >= f.HpAt)
            {
                if (f.Hp is < 90 && percent >= 99.5) { End(f, signal with { Kind = BossSignalKind.Reset, Source = "HP reset" }); return; }
                f.Hp = percent; f.HpAt = signal.At;
                foreach (var m in f.Profile?.Mechanics ?? [])
                    if (m.HpBelowPercent is { } threshold && percent <= threshold && (m.Phase == null || m.Phase == f.Phase) && f.HpRules.Add(m.Id))
                    {
                        if (m.SetPhase is { } phase) SetPhase(f, phase, signal.At);
                        if (Enabled(f, m)) { Alert(f, "hp/" + m.Id, m.Reaction.Length > 0 ? m.Reaction : m.Name, m.Category, signal.At, true, false);
                            if (m.FirstSeconds is { } delay) Schedule(f, m, signal.At, delay); }
                    }
            }
            else if (signal.Kind == BossSignalKind.Phase && signal.Phase is > 0 and <= 20) { SetPhase(f, signal.Phase, signal.At); return; }
            Log(f, signal);
        }
    }
    private void SetPhase(Fight f, int phase, long at)
    {
        if (f.Phase == phase) return;
        f.Phase = phase;
        _recent.RemoveAll(a => a.Predicted && a.Key.StartsWith(f.Id.ToString(), StringComparison.Ordinal));
        var pending = _pending.Where(a => !a.Predicted || !a.Key.StartsWith(f.Id.ToString(), StringComparison.Ordinal)).ToArray();
        _pending.Clear(); foreach (var alert in pending) _pending.Enqueue(alert);
        foreach (var key in _bars.Where(p => p.Value.Fight == f && p.Value.Kind != "cast").Select(p => p.Key).ToArray()) _bars.Remove(key);
        foreach (var m in f.Profile?.Mechanics ?? []) if (m.Phase == phase && m.FirstSeconds is { } delay && Enabled(f, m)) Schedule(f, m, at, delay);
        Log(f, f.Pull with { At = at, Kind = BossSignalKind.Phase, Phase = phase, Source = "Profile phase" });
    }
    private void Log(Fight f, BossSignal signal)
    {
        // Combat activity is always processed. Only repetitive journal samples are thinned;
        // dense raid hits must not exhaust the log before later casts/phases arrive.
        if (signal.Kind == BossSignalKind.Hit) {
            if (f.JournalHits.TryGetValue(signal.SkillId, out var previous) && signal.At - previous < 500) return;
            if (f.JournalHits.Count >= 128 && !f.JournalHits.ContainsKey(signal.SkillId)) f.JournalHits.Remove(f.JournalHits.MinBy(p => p.Value).Key);
            f.JournalHits[signal.SkillId] = signal.At;
        }
        if (signal.Kind == BossSignalKind.Hp) { if (signal.At - f.JournalHpAt < 500) return; f.JournalHpAt = signal.At; }
        Record?.Invoke(new(1, f.Id, signal with { Phase = f.Phase }));
    }
    private void Schedule(Fight f, BossMechanic rule, long at, double seconds, string? kind = null)
    {
        if (!double.IsFinite(seconds) || seconds is <= 0 or > 3600 || _bars.Count >= 128) return;
        kind ??= rule.Confidence < .8 ? "uncertain" : "predicted";
        var key = f.Id + "/" + kind + "/" + MechanicKey(f.Profile, rule);
        var warnings = (_options.WarningSeconds ?? rule.WarningSeconds).Where(x => x > 0 && x <= Math.Min(60, seconds) && double.IsFinite(x)).Distinct().OrderDescending().ToArray();
        if (_bars.TryGetValue(key, out var existing))
        { existing.Started = at; existing.Due = at + (long)(seconds * 1000); return; } // warning ledger survives re-anchoring
        _bars[key] = new() { Key = key, Label = rule.Name, Reaction = rule.Reaction, Category = rule.Category, Kind = kind, Fight = f,
            Skill = rule.SkillId, Started = at, Due = at + (long)(seconds * 1000), Confidence = rule.Confidence,
            Samples = rule.Samples, Evidence = rule.Evidence.Length > 0 ? rule.Evidence : f.Profile?.Evidence ?? "", Warnings = warnings };
    }
    private void Alert(Fight f, string key, string text, string category, long at, bool sound, bool predicted, double seconds = 4.5)
    {
        if (string.IsNullOrWhiteSpace(text)) text = "Umiejętność bossa";
        var alert = new BossAlert(f.Id + "/" + key, text, category, at, sound, predicted, f.Pull.Synthetic) { LifetimeMs = (long)(Math.Clamp(seconds, .5, 30) * 1000) };
        if (_recent.Any(a => a.Key == alert.Key)) return;
        _recent.Add(alert); if (_recent.Count > 16) _recent.RemoveAt(0);
        if (_pending.Count < 32) _pending.Enqueue(alert);
    }
    private void End(Fight f, BossSignal signal)
    {
        Log(f, signal); _fights.Remove(f.Pull.Actor);
        foreach (var key in _bars.Where(p => p.Value.Fight == f).Select(p => p.Key).ToArray()) _bars.Remove(key);
        _recent.RemoveAll(a => a.Key.StartsWith(f.Id.ToString(), StringComparison.Ordinal));
        _observed.RemoveAll(a => a.Key.StartsWith(f.Id.ToString(), StringComparison.Ordinal));
        var pending = _pending.Where(a => !a.Key.StartsWith(f.Id.ToString(), StringComparison.Ordinal)).ToArray(); _pending.Clear(); foreach (var alert in pending) _pending.Enqueue(alert);
    }
    public void Reset(long at, string reason)
    {
        lock (_gate) { foreach (var f in _fights.Values.ToArray()) End(f, f.Pull with { At = Math.Max(at, f.LastActivity), Kind = BossSignalKind.Reset, Source = reason }); _recent.Clear(); _pending.Clear(); _observed.Clear(); }
    }
    public void Tick(long now)
    {
        lock (_gate)
        {
            foreach (var f in _fights.Values.ToArray()) if (now - f.LastActivity >= (f.Profile?.ResetAfterSeconds ?? 45) * 1000)
                End(f, f.Pull with { At = now, Kind = BossSignalKind.Reset, Source = "Idle timeout" });
            foreach (var bar in _bars.Values.ToArray())
            {
                var remaining = (bar.Due - now) / 1000d;
                if (remaining < (bar.Kind == "cast" ? 0 : bar.Kind == "windup" ? -.75 : -5)) { _bars.Remove(bar.Key); continue; }
                var crossed = bar.Warnings.Where(w => remaining >= 0 && remaining <= w && bar.Previous > w && bar.Warned.Add(w)).ToArray();
                if (crossed.Length > 0)
                {
                    var threshold = crossed.Min();
                    Alert(bar.Fight, bar.Key + "/warn/" + threshold, (bar.Reaction.Length > 0 ? bar.Reaction : bar.Label) + $" · {Math.Ceiling(Math.Max(0, remaining)):0} s",
                        bar.Category, now, bar.Confidence >= .8, true);
                }
                bar.Previous = remaining;
            }
            _recent.RemoveAll(a => now - a.At > a.LifetimeMs);
            _observed.RemoveAll(s => now - s.At > 5000);
        }
    }
    public BossAssistantSnapshot Snapshot(long now)
    {
        Tick(now);
        lock (_gate) return new(_fights.Values.Select(f => new BossFightSnapshot(f.Id, f.Pull.Actor, f.Pull.NpcCode, f.Pull.BossName, f.Pull.MapId, f.Phase, f.Hp, f.Pull.At, f.Pull.PullConfirmed)).ToArray(),
            _bars.Values.OrderBy(b => b.Due).Select(b => new BossBarSnapshot(b.Key, b.Fight.Pull.Actor, b.Skill, b.Fight.Pull.BossName, b.Label, b.Reaction, b.Category, b.Kind,
                b.Started, b.Due, Math.Max(0, (b.Due - now) / 1000d), Math.Clamp((b.Kind == "windup" ? now - b.Started : b.Due - now) / (double)Math.Max(1, b.Due - b.Started), 0, 1), b.Confidence, b.Samples, b.Evidence)).ToArray(), _recent.ToArray(), now)
                { ObservedSkills = _observed.OrderByDescending(s => s.At).ToArray() };
    }
    public IReadOnlyList<BossAlert> TakeAlerts() { lock (_gate) { var a = _pending.ToArray(); _pending.Clear(); return a; } }
}
