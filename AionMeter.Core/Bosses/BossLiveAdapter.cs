using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Bosses;

/// <summary>Uses Aurum's decoded events; never reads packets or elects a player from nearby actors.</summary>
public sealed class BossLiveAdapter
{
    private sealed class Npc
    {
        internal required uint Actor;
        internal required int Code;
        internal required string Name;
        internal long Max, Hp = -1, BattleAt, HpAt;
    }
    private readonly object _gate = new();
    private readonly GameData _data;
    private readonly BossAssistantEngine _engine;
    private readonly Func<uint, long, bool> _member;
    private readonly Func<BossSnapshot?> _context;
    private readonly Dictionary<uint, Npc> _npcs = new();
    private readonly Dictionary<uint, int> _otherNpcs = new();
    private readonly List<BossSignal> _unmapped = new();
    private readonly Dictionary<(uint Actor, int Skill), long> _unmappedAt = new();
    public object Diagnostics { get { lock (_gate) return new { MapId = _map, BossActors = _npcs.Count, OtherNpcActors = _otherNpcs.Count, UnmappedCasts = _unmapped.ToArray() }; } }
    private int _map;
    public bool Enabled { get; set; } = true;
    public BossLiveAdapter(GameData data, BossAssistantEngine engine, Func<uint, long, bool> member, Func<BossSnapshot?> context, int map = 0)
    { _data = data; _engine = engine; _member = member; _context = context; _map = map; }
    public void Clear(long at, string reason) { lock (_gate) { _npcs.Clear(); _otherNpcs.Clear(); _unmapped.Clear(); _unmappedAt.Clear(); _engine.Reset(at, reason); } }
    private Npc? Find(uint actor)
    {
        if (_npcs.TryGetValue(actor, out var npc)) return npc;
        var boss = _context();
        if (boss?.ActorId != actor || !_data.Npcs.TryGetValue(boss.NpcCode, out var def) || !def.IsBoss) return null;
        npc = new() { Actor = actor, Code = boss.NpcCode, Name = boss.Name, Hp = boss.Hp, Max = boss.MaxHpKnown ? boss.MaxHp : 0 };
        _npcs[actor] = npc; return npc;
    }
    private BossSignal Signal(Npc npc, long at, BossSignalKind kind, int skill = 0) => new(at, kind, npc.Actor, npc.Code)
    { BossName = npc.Name, MapId = _map, SkillId = skill, SkillName = skill == 0 ? "" : _data.SkillName(skill),
        HpPercent = npc.Max > 0 && npc.Hp >= 0 ? Math.Clamp(npc.Hp * 100d / npc.Max, 0, 100) : null, Source = "Aurum decoded events" };
    private void Engage(Npc npc, long at)
    {
        if (_engine.HasFight(npc.Actor)) return;
        var exact = npc.BattleAt > 0 && at - npc.BattleAt is >= 0 and <= 2000;
        _engine.Process(Signal(npc, exact ? npc.BattleAt : at, BossSignalKind.Pull) with { PullConfirmed = exact });
    }
    private void Remove(uint actor, long at)
    {
        _otherNpcs.Remove(actor);
        if (_npcs.Remove(actor, out var old)) _engine.Process(Signal(old, at, BossSignalKind.Reset));
    }
    public void Process(GameEvent ev)
    {
        lock (_gate)
        {
            switch (ev)
            {
                case LocalIdentityLostEvent:
                    Clear(ev.TimeMs, "Identity changed"); return;
                case ZoneChangedEvent zone:
                    if (_map != 0 && zone.MapId != _map) Clear(ev.TimeMs, "Zone changed"); _map = zone.MapId; return;
                case SummonSeenEvent summon: Remove(summon.ActorId, ev.TimeMs); return;
                case PlayerSeenEvent player: Remove(player.ActorId, ev.TimeMs); return;
                case NpcSeenEvent seen:
                    if (_data.Npcs.TryGetValue(seen.NpcCode, out var def) && def.IsBoss && !def.IsDummy)
                    {
                        _otherNpcs.Remove(seen.ActorId);
                        if (_npcs.Count >= 256 && !_npcs.ContainsKey(seen.ActorId)) return;
                        if (_npcs.TryGetValue(seen.ActorId, out var old) && old.Code == seen.NpcCode) { if (seen.MaxHp > 0) old.Max = seen.MaxHp; }
                        else {
                            if (old != null) _engine.Process(Signal(old, ev.TimeMs, BossSignalKind.Reset));
                            _npcs[seen.ActorId] = new() { Actor = seen.ActorId, Code = seen.NpcCode, Name = string.IsNullOrWhiteSpace(seen.Name) ? def.Name : seen.Name, Max = seen.MaxHp };
                        }
                    }
                    else { Remove(seen.ActorId, ev.TimeMs); if (_otherNpcs.Count < 512 && seen.NpcCode > 0) _otherNpcs[seen.ActorId] = seen.NpcCode; }
                    return;
                case NpcHpEvent hp when Find(hp.ActorId) is { } npc:
                    if (ev.TimeMs < npc.HpAt) return;
                    npc.HpAt = ev.TimeMs;
                    npc.Hp = hp.CurrentHp; if (hp.MaxHp > 0) npc.Max = hp.MaxHp;
                    if (Enabled && npc.Max > 0 && hp.CurrentHp >= 0) _engine.Process(Signal(npc, ev.TimeMs, BossSignalKind.Hp));
                    return;
                case BattleStateEvent battle when Find(battle.ActorId) is { } npc:
                    if (battle.InCombat) npc.BattleAt = ev.TimeMs;
                    else { npc.BattleAt = 0; if (Enabled) _engine.Process(Signal(npc, ev.TimeMs, BossSignalKind.Reset)); }
                    return;
                case DeathEvent death when Find(death.ActorId) is { } npc:
                    if (Enabled) _engine.Process(Signal(npc, ev.TimeMs, death.AlreadyDead ? BossSignalKind.Reset : BossSignalKind.Death));
                    _npcs.Remove(death.ActorId); return;
            }
            if (!Enabled) return;
            if (ev is DeathEvent otherDeath) _otherNpcs.Remove(otherDeath.ActorId);
            if (ev is CastEvent cast && Find(cast.ActorId) is { } caster)
            {
                if (!_engine.HasFight(caster.Actor) && !_member(cast.TargetId, ev.TimeMs) && _context()?.ActorId != caster.Actor) return;
                Engage(caster, ev.TimeMs); _engine.Process(Signal(caster, ev.TimeMs, BossSignalKind.Cast, cast.SkillCode));
            }
            else if (ev is CastEvent unknown && _otherNpcs.TryGetValue(unknown.ActorId, out var code) && _context() is { } context &&
                _engine.HasFight(context.ActorId) && Find(context.ActorId) is { } activeBoss)
            {
                // This caster is not the boss. Preserve the source as unverified
                // diagnostic evidence, never as a boss cast, warning or learned timer.
                var key = (unknown.ActorId, unknown.SkillCode);
                if (_unmappedAt.TryGetValue(key, out var previous) && unknown.TimeMs - previous < 700) return;
                if (_unmappedAt.Count >= 256) _unmappedAt.Remove(_unmappedAt.MinBy(p => p.Value).Key);
                _unmappedAt[key] = unknown.TimeMs;
                var signal = Signal(activeBoss, unknown.TimeMs, BossSignalKind.UnmappedCast, unknown.SkillCode) with
                { SourceActor = unknown.ActorId, SourceNpcCode = code, Confidence = 0, Source = "Nearby NPC cast; boss association unverified" };
                _unmapped.Add(signal); if (_unmapped.Count > 16) _unmapped.RemoveAt(0);
                _engine.Process(signal);
            }
            else if (ev is DamageEvent damage) Hit(damage.SourceId, damage.TargetId, damage.SkillCode, damage.TimeMs);
            else if (ev is HitNoticeEvent hit) Hit(hit.ActorId, hit.TargetId, hit.SkillCode, hit.TimeMs);
        }
    }
    private void Hit(uint source, uint target, int skill, long at)
    {
        if (Find(target) is { } boss && _member(source, at))
        { Engage(boss, at); _engine.Process(Signal(boss, at, BossSignalKind.Hit) with { Source = "Own group contact" }); }
        else if (Find(source) is { } caster && (_engine.HasFight(source) || _member(target, at)))
        { Engage(caster, at); _engine.Process(Signal(caster, at, BossSignalKind.Hit, skill)); }
    }
}
