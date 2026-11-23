using System.Text.Json;
using AionMeter.Core.Events;

namespace AionMeter.Core.Pets;

public sealed record PetDrop(int NpcCode, string NpcName, double Chance);
public sealed record PetDefinition(int Id, string Name, IReadOnlyList<PetDrop> Drops, string Genus = "unknown");
public sealed record PetState(int Level, int Points)
{
    public int Remaining => Level >= 3 ? 0 : Math.Max(0, Costs.Skip(Level).Sum() - Points);
    public static readonly int[] Costs = [5, 25, 75];
    public PetState Gain(int amount)
    {
        var level = Level; var points = Points + amount;
        while (level < 3 && points >= Costs[level]) { points -= Costs[level]; level++; }
        return new(level, points);
    }
}
public sealed record PetNotice(string PetName, int? Remaining, int Level, int Points,
    int? Mobs, bool Estimated, bool Synced, string? NpcName, int Gained = 0, int PetId = 0);

public sealed class PetProgress
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PetDefinition> _catalog;
    private readonly Dictionary<int, PetState> _pets = new();
    private readonly Dictionary<uint, int> _npcs = new();
    private readonly Dictionary<uint, uint> _owners = new();
    private readonly Dictionary<uint, long> _ownHits = new();
    private uint _self;
    private string? _key;
    private bool _synced;
    private bool _awaitingIdentity;
    private PetCollectionEvent? _pendingCollection;
    private (int NpcCode, long At)? _lastKill;
    private readonly Dictionary<int, long> _notifyPets = new();
    private long _notifyAt;
    private readonly Dictionary<int, int> _gains = new();
    private int _version;
    private bool _fromCache;
    public bool FromCache { get { lock (_gate) return _fromCache; } }
    public PetProgressSnapshot Snapshot()
    {
        lock (_gate) return new(_key ?? "", _self, _synced, _version, new(_pets), DateTimeOffset.UtcNow, _fromCache);
    }
    public bool Restore(PetProgressSnapshot snapshot, string? expectedProfile = null)
    {
        if (!PetStateStore.IsValid(snapshot) || expectedProfile != null && expectedProfile != snapshot.Profile) return false;
        lock (_gate)
        {
            _key = snapshot.Profile; _self = 0; _synced = true; _fromCache = true;
            _pets.Clear(); foreach (var pet in snapshot.Pets) _pets[pet.Key] = pet.Value;
            _version++;
        }
        return true;
    }
    public int Version { get { lock (_gate) return _version; } }
    public object DiagnosticState()
    {
        return Snapshot();
    }
    public bool Synced { get { lock (_gate) return _synced; } }
    public event Action? Changed;

    public PetProgress(IEnumerable<PetDefinition> catalog) => _catalog = catalog.ToDictionary(p => p.Id);
    public static PetProgress LoadCatalog(string path) => new(File.Exists(path)
        ? JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(path)) ?? [] : []);
    public PetState? State(int pet) { lock (_gate) return _synced ? _pets.GetValueOrDefault(pet, new(0, 0)) : null; }

    public void Process(GameEvent gameEvent)
    {
        var changed = false;
        lock (_gate)
        {
            switch (gameEvent)
            {
                case LocalIdentityLostEvent:
                    _self = 0; _awaitingIdentity = true; _pendingCollection = null;
                    _ownHits.Clear(); _owners.Clear(); break;
                case ZoneChangedEvent:
                    _awaitingIdentity = true; _pendingCollection = null;
                    _npcs.Clear(); _ownHits.Clear(); _owners.Clear(); _lastKill = null;
                    break;
                case SelfIdentifiedEvent self when self.ServerId > 0 && !string.IsNullOrEmpty(self.Name):
                    _self = self.ActorId;
                    var key = self.ServerId + ":" + self.Name;
                    if (_key != key)
                    {
                        _key = key; _synced = false; _fromCache = false;
                        _pets.Clear(); _notifyPets.Clear(); _gains.Clear(); changed = true;
                    }
                    _awaitingIdentity = false;
                    if (_pendingCollection is { } pending) { Apply(pending); _pendingCollection = null; changed = true; }
                    break;
                case PetCollectionEvent collection:
                    if (_key is null || _awaitingIdentity) _pendingCollection = collection;
                    else { Apply(collection); changed = true; }
                    break;
                case PetSoulsEvent souls:
                    foreach (var gain in souls.Gains.Where(g => g.Value > 0))
                    {
                        if (_synced)
                        {
                            var before = _pets.GetValueOrDefault(gain.PetId, new(0, 0));
                            if (before.Level < 3)
                            {
                                _pets[gain.PetId] = before.Gain(gain.Value);
                                changed = true;
                            }
                        }
                        _gains[gain.PetId] = _gains.GetValueOrDefault(gain.PetId) + gain.Value;
                        _notifyPets[gain.PetId] = souls.TimeMs;
                        _notifyAt = souls.TimeMs + 200;
                    }
                    break;
                case PetLevelEvent level when _synced:
                    var was = _pets.GetValueOrDefault(level.PetId, new(0, 0));
                    if (level.Level <= was.Level) break; // predicted level-up already applied; no double subtraction
                    _pets[level.PetId] = new(level.Level, level.Unlock ? was.Points
                        : Math.Max(0, was.Points - PetState.Costs.Skip(was.Level).Take(level.Level - was.Level).Sum()));
                    changed = true;
                    break;
                case NpcSeenEvent npc:
                    if (_npcs.Count > 10000) _npcs.Clear();
                    _npcs[npc.ActorId] = npc.NpcCode;
                    break;
                case SummonSeenEvent summon when summon.OwnerId != 0: _owners[summon.ActorId] = summon.OwnerId; break;
                case DamageEvent hit when _self != 0 && (hit.SourceId == _self || _owners.GetValueOrDefault(hit.SourceId) == _self):
                    if (_ownHits.Count > 10000) _ownHits.Clear();
                    _ownHits[hit.TargetId] = hit.TimeMs;
                    break;
                case DeathEvent death when !death.AlreadyDead:
                    if (_npcs.TryGetValue(death.ActorId, out var code) && _ownHits.Remove(death.ActorId, out var at)
                        && death.TimeMs >= at && death.TimeMs - at <= 20000)
                    {
                        _lastKill = (code, death.TimeMs);
                    }
                    break;
            }
        }
        if (changed) { lock (_gate) _version++; Changed?.Invoke(); }
    }

    private void Apply(PetCollectionEvent collection)
    {
        _pets.Clear();
        foreach (var level in collection.Levels) _pets[level.PetId] = new(level.Value, 0);
        foreach (var point in collection.Points)
        {
            var previous = _pets.GetValueOrDefault(point.PetId, new(0, 0));
            _pets[point.PetId] = previous with { Points = point.Value };
        }
        _synced = true;
        _fromCache = false;
    }

    public IReadOnlyList<PetNotice> TakeNotices(long now)
    {
        lock (_gate)
        {
            if (_notifyPets.Count == 0 || now < _notifyAt) return [];
            var notices = new List<PetNotice>();
            foreach (var (id, at) in _notifyPets.OrderByDescending(p => p.Value))
            {
                if (now - at > 10000) continue;
                var definition = _catalog.GetValueOrDefault(id);
                var state = _pets.GetValueOrDefault(id, new(0, 0));
                var drop = _lastKill is { } kill && Math.Abs(at - kill.At) < 5000
                    ? definition?.Drops.FirstOrDefault(d => d.NpcCode == kill.NpcCode) : null;
                var chance = drop?.Chance ?? 0;
                var mobs = _synced && chance > 0 ? (int?)Math.Ceiling(state.Remaining / chance) : null;
                notices.Add(new(definition?.Name ?? "Pet #" + id, _synced ? state.Remaining : null,
                    state.Level, state.Points, mobs, chance is > 0 and < 1, _synced, drop?.NpcName, _gains.GetValueOrDefault(id), id));
            }
            _notifyPets.Clear(); _gains.Clear();
            return notices;
        }
    }
}

