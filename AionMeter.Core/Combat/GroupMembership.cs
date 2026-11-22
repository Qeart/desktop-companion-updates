using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Combat;

// Only the own-group roster authorizes members. Auxiliary actor channels are
// diagnostic observations: the Global solo test also saw unrelated objects.
internal sealed class GroupMembership
{
    private readonly HashSet<(string Name, int Server)> _members = [];
    private readonly Dictionary<uint, long> _presence = [];
    private readonly Queue<PartyRosterEvent> _pending = new();
    private (string Name, int Server)? _self;
    private uint? _groupId;
    public int Version { get; private set; }
    public int NamedMembers => _members.Count;
    public int RecentActors(long now) => _presence.Count(p => now - p.Value <= 10_000);
    private static string Key(string name) => name.Trim().ToUpperInvariant();
    public void SetSelf(PlayerInfo? self)
    {
        if (self == null || self.ServerId == 0 || string.IsNullOrWhiteSpace(self.Name)) return;
        var identity = (Key(self.Name), self.ServerId);
        if (_self is { } old && (old.Name != identity.Item1 || old.Server != 0 && identity.ServerId != 0 && old.Server != identity.ServerId)) Clear();
        _self = identity;
        var pending = _pending.ToArray(); _pending.Clear();
        foreach (var roster in pending) Roster(roster);
    }
    public void Clear()
    {
        _members.Clear(); _presence.Clear(); _pending.Clear(); _groupId = null; Version++;
    }
    public void ClearActorIds() => _presence.Clear();
    public void Seen(uint actor, long at)
    {
        if (actor == 0) return;
        _presence[actor] = Math.Max(at, _presence.GetValueOrDefault(actor));
        if (_presence.Count > 128)
            foreach (var id in _presence.Where(p => at - p.Value > 10_000).Select(p => p.Key).ToArray()) _presence.Remove(id);
    }
    public bool Roster(PartyRosterEvent roster)
    {
        if (_self == null)
        {
            _pending.Enqueue(roster); while (_pending.Count > 4) _pending.Dequeue(); return false;
        }
        var names = roster.Members.Where(m => !string.IsNullOrWhiteSpace(m.Name)).Select(m => (Key(m.Name), m.ServerId)).ToHashSet();
        var includesSelf = names.Any(m => m.Item1 == _self.Value.Name && (_self.Value.Server == 0 || m.ServerId == _self.Value.Server));
        var sameGroup = _groupId.HasValue && roster.GroupId != 0 && roster.GroupId == _groupId;
        if (roster.Complete && roster.Members.Count == 0 && (sameGroup || roster.GroupId == 0)) { Clear(); return true; }
        if (!includesSelf && !sameGroup) return false; // party finder / somebody else's roster
        if (!includesSelf && roster.Complete) { Clear(); return true; }
        if (!sameGroup && includesSelf && _groupId.HasValue) { _members.Clear(); _presence.Clear(); }
        var replacement = roster.Complete ? names : _members.Union(names).ToHashSet();
        if (_members.SetEquals(replacement) && _groupId == roster.GroupId) return true;
        _members.Clear(); foreach (var member in replacement) _members.Add(member);
        _groupId = roster.GroupId; _presence.Clear(); Version++; return true;
    }
    public bool Contains(uint actor, PlayerInfo? identity, uint? selfId, long now)
    {
        if (selfId == actor) return true;
        if (identity == null || string.IsNullOrWhiteSpace(identity.Name)) return false;
        var name = Key(identity.Name);
        return identity.ServerId != 0 && _members.Contains((name, identity.ServerId));
    }
}
