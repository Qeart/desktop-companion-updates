using AionMeter.Core.Events;

namespace AionMeter.Core.Game;

public sealed record KnownCharacter(string Name, int ServerId, GameClass Class);
public sealed record LocalPlayerState(string Stage, string Source, uint? ActorId, string Name, int ServerId,
    GameClass Class, int StatsStreak, long StatsRecords, int BufferedEvents, int RememberedCharacters);

/// <summary>
/// Recover the local player without requiring the login SelfInfo record. Only
/// own-stat records bind an unnamed actor; nearby players, loot and cached ids
/// cannot elect themselves. All consumers receive the same resolved identity.
/// </summary>
public sealed class LocalPlayerResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, SelfIdentifiedEvent> _players = [];
    private readonly Dictionary<uint, (GameClass Class, int Count)> _classes = [];
    private readonly HashSet<uint> _nonPlayers = [];
    private readonly Dictionary<(string Name, int Server), KnownCharacter> _known = [];
    private readonly Queue<GameEvent> _pending = new();
    private SelfIdentifiedEvent? _self, _published;
    private uint _candidate;
    private int _streak, _knownVersion;
    private long _lastStatsAt, _statsRecords;
    private string _source = "AwaitingLiveIdentity";
    public event Action<GameEvent>? EventDecoded;
    public LocalPlayerResolver(IEnumerable<KnownCharacter>? known = null)
    {
        foreach (var c in known ?? []) Remember(c);
    }
    private static (string, int) Key(string name, int server) => (name.Trim().ToUpperInvariant(), server);
    public int KnownVersion { get { lock (_gate) return _knownVersion; } }
    public KnownCharacter[] KnownCharacters { get { lock (_gate) return _known.Values.ToArray(); } }
    public LocalPlayerState State { get { lock (_gate) return new(
        _self == null ? "AwaitingLiveIdentity" : _self.Class == GameClass.Unknown ? "AwaitingOwnSkill" : "Ready",
        _source, _self?.ActorId, _self?.Name ?? "", _self?.ServerId ?? 0, _self?.Class ?? GameClass.Unknown,
        _streak, _statsRecords, _pending.Count, _known.Count); } }

    public void Process(GameEvent ev)
    {
        lock (_gate)
        {
            if (ev is ZoneChangedEvent { IsTeleport: false })
            {
                _players.Clear(); _classes.Clear(); _nonPlayers.Clear(); _pending.Clear();
                _self = _published = null; _candidate = 0; _streak = 0; _lastStatsAt = 0; _source = "ZoneChange";
                Emit(new LocalIdentityLostEvent(ev.TimeMs, IsZoneChange: true)); Emit(ev); return;
            }
            switch (ev)
            {
                case SelfIdentifiedEvent own:
                    _candidate = 0; _streak = 0; _nonPlayers.Remove(own.ActorId);
                    _players[own.ActorId] = own; _self = Enrich(own); _source = "SelfRecord";
                    Publish(ev.TimeMs); Flush(); return;
                case OwnStatsEvent stats:
                    _statsRecords++;
                    if (_published != null && stats.TimeMs < _published.TimeMs) break;
                    if (_nonPlayers.Contains(stats.ActorId)) { _candidate = 0; _streak = 0; break; }
                    if (_self?.ActorId == stats.ActorId) { _candidate = 0; _streak = 0; break; }
                    _streak = _candidate == stats.ActorId && stats.TimeMs >= _lastStatsAt && stats.TimeMs - _lastStatsAt <= 10_000 ? _streak + 1 : 1;
                    _candidate = stats.ActorId; _lastStatsAt = stats.TimeMs;
                    if (_streak >= 3)
                    {
                        // No name/class copied from the previous actor or last session.
                        _self = Enrich(_players.GetValueOrDefault(stats.ActorId) ?? new(stats.TimeMs, stats.ActorId, "", 0, GameClass.Unknown));
                        _source = "OwnStats"; _candidate = 0; _streak = 0; Publish(ev.TimeMs); Flush();
                    }
                    break;
                case PlayerSeenEvent player:
                    if (_players.Count >= 2048 && !_players.ContainsKey(player.ActorId)) break;
                    var seen = new SelfIdentifiedEvent(player.TimeMs, player.ActorId, player.Name, player.ServerId, player.Class);
                    if (_players.TryGetValue(player.ActorId, out var old) && old.Name == player.Name)
                        seen = seen with { ServerId = player.ServerId != 0 ? player.ServerId : old.ServerId,
                            Class = player.Class != GameClass.Unknown ? player.Class : old.Class, Level = old.Level, CombatPower = old.CombatPower };
                    _players[player.ActorId] = seen; _nonPlayers.Remove(player.ActorId);
                    if (_self?.ActorId == player.ActorId) { _self = Enrich(seen); Publish(ev.TimeMs); }
                    break;
                case NpcSeenEvent npc:
                    Block(npc.ActorId); break;
                case SummonSeenEvent summon:
                    Block(summon.ActorId); break;
            }
            NoteClass(ev);
            if (_self == null && ev is DamageEvent or HealEvent or CastEvent or HitNoticeEvent)
            {
                while (_pending.Count > 0 && ev.TimeMs - _pending.Peek().TimeMs > 15_000) _pending.Dequeue();
                if (_pending.Count >= 1024) _pending.Dequeue();
                _pending.Enqueue(ev); return;
            }
            Emit(ev);
        }
    }
    private void Block(uint actor)
    {
        if (_players.ContainsKey(actor) || _self?.ActorId == actor) return;
        if (_nonPlayers.Count < 4096) _nonPlayers.Add(actor);
        _classes.Remove(actor);
    }
    private void NoteClass(GameEvent ev)
    {
        var (actor, skill, cast) = ev switch {
            CastEvent e => (e.ActorId, e.SkillCode, true),
            DamageEvent e => (e.SourceId, e.SkillCode, false),
            HealEvent e => (e.SourceId, e.SkillCode, false),
            _ => (0u, 0, false)
        };
        var cls = GameData.ClassFromSkill(skill);
        if (actor == 0 || cls == GameClass.Unknown || _nonPlayers.Contains(actor)) return;
        if (_classes.Count >= 2048 && !_classes.ContainsKey(actor)) return;
        var old = _classes.GetValueOrDefault(actor);
        var count = cast ? 2 : old.Class == cls ? Math.Min(2, old.Count + 1) : 1;
        _classes[actor] = (cls, count);
        if (_self?.ActorId == actor && _self.Class == GameClass.Unknown && count >= 2)
        { _self = Enrich(_self); Publish(ev.TimeMs); }
    }
    private SelfIdentifiedEvent Enrich(SelfIdentifiedEvent self)
    {
        if (self.Class == GameClass.Unknown && _classes.TryGetValue(self.ActorId, out var evidence) && evidence.Count >= 2)
            self = self with { Class = evidence.Class };
        if (self.Class == GameClass.Unknown && self.ServerId != 0 && _known.TryGetValue(Key(self.Name, self.ServerId), out var known))
            self = self with { Class = known.Class };
        return self;
    }
    private void Publish(long at)
    {
        if (_self == null) return;
        _self = _self with { TimeMs = at };
        var current = _self with { FreshIdentity = true };
        if (_published is { } previous && previous.ActorId == current.ActorId && previous.Name == current.Name
            && previous.ServerId == current.ServerId && previous.Class == current.Class && previous.CombatPower == current.CombatPower) return;
        if (_published is { } before && before.ActorId != current.ActorId) Emit(new LocalIdentityLostEvent(at));
        _published = current;
        Remember(new(current.Name, current.ServerId, current.Class)); Emit(current);
    }
    private void Remember(KnownCharacter c)
    {
        if (string.IsNullOrWhiteSpace(c.Name) || c.ServerId is < 1000 or >= 3000 || c.Class == GameClass.Unknown || !Enum.IsDefined(c.Class)) return;
        var key = Key(c.Name, c.ServerId);
        if (_known.TryGetValue(key, out var previous) && previous == c || _known.Count >= 64 && !_known.ContainsKey(key)) return;
        _known[key] = c; _knownVersion++;
    }
    private void Flush()
    {
        var pending = _pending.ToArray(); _pending.Clear();
        var now = _published?.TimeMs ?? 0;
        foreach (var ev in pending) if (now - ev.TimeMs <= 15_000) Emit(ev);
    }
    private void Emit(GameEvent ev) => EventDecoded?.Invoke(ev);
}
