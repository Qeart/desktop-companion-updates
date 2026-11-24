using System.Text.Json;
using AionMeter.Core.Bosses;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace Aurum.Tests;

public class BossCastVisibilityTests
{
    private sealed record RecordedCast(long At, int SkillId);
    private static BossSignal Signal(long at, BossSignalKind kind, int skill = 0, string name = "") => new(at, kind, 700, 2300171)
        { BossName = "Ultimate Berk", MapId = 600001, SkillId = skill, SkillName = name };
    private static BossAssistantEngine Engine() => new(BossProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "boss-data", "bossmods")));
    [Fact]
    public void Recorded_Berk_Rush_is_visible_without_a_timer_rule_or_an_invented_cooldown()
    {
        // Real NPC cast order/gaps from the reported 2026-11-24 fight. Epoch,
        // actor/player IDs, session IDs and network data have been removed.
        var casts = JsonSerializer.Deserialize<RecordedCast[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "berk-casts.json")), BossJson.Options)!;
        var data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "boss-data")); var engine = Engine();
        engine.Process(Signal(1000, BossSignalKind.Pull));
        foreach (var cast in casts.Where(c => c.At <= 23752)) {
            engine.Process(Signal(cast.At, BossSignalKind.Cast, cast.SkillId, data.SkillName(cast.SkillId)));
            if (cast.SkillId is 1800030 or 1800031) {
                var state = engine.Snapshot(cast.At); Assert.Single(state.ObservedSkills, s => s.Name == "Rush");
                Assert.DoesNotContain(state.Bars, b => b.SkillId is 1800030 or 1800031);
            }
        }
        Assert.DoesNotContain(engine.TakeAlerts(), a => a.Text.Contains("Rush"));
        Assert.Single(engine.Snapshot(23752).ObservedSkills, s => s.Name == "Rush");
    }
    [Fact]
    public void A_boss_with_no_profile_can_show_the_actual_skill_without_forecasts_or_sound()
    {
        var engine = new BossAssistantEngine(new([])); engine.Process(Signal(1000, BossSignalKind.Pull));
        engine.Process(Signal(2000, BossSignalKind.Cast, 1800030, "Rush"));
        var state = engine.Snapshot(2000); Assert.Equal("Rush", Assert.Single(state.ObservedSkills).Name); Assert.Empty(state.Bars); Assert.Empty(engine.TakeAlerts());
    }
    [Fact]
    public void Observed_skills_expire_without_packets_and_are_removed_on_death_or_disable()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Process(Signal(2000, BossSignalKind.Cast, 1800030, "Rush"));
        Assert.Empty(engine.Snapshot(7001).ObservedSkills);
        engine.Process(Signal(8000, BossSignalKind.Cast, 1800030, "Rush")); engine.Configure(new() { ShowObservedSkills = false }); Assert.Empty(engine.Snapshot(8000).ObservedSkills);
        engine.Configure(new()); engine.Process(Signal(9000, BossSignalKind.Cast, 1800030, "Rush")); engine.Process(Signal(10000, BossSignalKind.Death));
        Assert.Empty(engine.Snapshot(10000).ObservedSkills);
    }
    [Fact]
    public void Observed_names_are_bounded_and_raw_ids_remain_in_the_journal()
    {
        var engine = Engine(); var records = new List<BossLogRecord>(); engine.Record += records.Add;
        engine.Process(Signal(1000, BossSignalKind.Pull));
        for (var i = 0; i < 10; i++) engine.Process(Signal(2000 + i * 10, BossSignalKind.Cast, 1800100 + i, "Skill " + i));
        Assert.Equal(4, engine.Snapshot(2200).ObservedSkills.Count); Assert.Equal(10, records.Count(r => r.Signal.Kind == BossSignalKind.Cast));
    }
    [Fact]
    public void An_observed_skill_cannot_replace_a_mechanic_warning_or_create_audio()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull));
        engine.Process(Signal(2000, BossSignalKind.Cast, 1800053, "Wave of Fury")); engine.TakeAlerts();
        engine.Process(Signal(3000, BossSignalKind.Cast, 1800030, "Rush")); var state = engine.Snapshot(3000);
        Assert.Equal("Wave of Fury", Assert.Single(state.Alerts).Text); Assert.Equal("Rush", state.ObservedSkills[0].Name); Assert.Empty(engine.TakeAlerts());
    }
    [Fact]
    public void A_confirmed_boss_actor_can_also_show_a_player_shaped_skill_code()
    {
        // Watcher Kaira uses this code in the recorded live events. Classification
        // belongs to the actor; its observed casts need not all be seven digits.
        var engine = new BossAssistantEngine(new([]));
        engine.Process(new(1000, BossSignalKind.Pull, 700, 2600089) { BossName = "Watcher Kaira" });
        engine.Process(new(2000, BossSignalKind.Cast, 700, 2600089) { BossName = "Watcher Kaira", SkillId = 16330007, SkillName = "Dimensional Control" });
        Assert.Equal("Dimensional Control", Assert.Single(engine.Snapshot(2000).ObservedSkills).Name);
    }
    [Fact]
    public void Display_name_fallback_does_not_change_raw_skill_ids_or_override_exact_names()
    {
        var data = GameData.Empty; data.Skills[1800050] = "Wave of Fury"; data.Skills[1800055] = "Impact Absorption";
        data.Skills[11020000] = "Player skill";
        Assert.Equal("Wave of Fury", data.SkillName(1800053)); Assert.Equal("Impact Absorption", data.SkillName(1800055));
        Assert.Equal(1800053, GameData.BaseSkill(1800053)); Assert.Equal("Player skill", data.SkillName(11020140));
    }
    [Theory]
    [InlineData(700u, 1800030, 0u, true)]
    [InlineData(700u, 1800030, 88u, true)]
    [InlineData(700u, 1800030, 9999999u, true)]
    [InlineData(700u, 1800030, 10000000u, false)]
    [InlineData(0u, 1800030, 0u, false)]
    [InlineData(700u, 0, 0u, false)]
    [InlineData(700u, 11020000, 0u, false)]
    public void Zero_target_NPC_casts_are_decoded_with_entity_and_skill_bounds_intact(uint actor, int skill, uint target, bool valid)
    {
        // Constructed layout fixture; the real Rush recording above already decoded
        // successfully. It does not prove the missing Rush had a zero target.
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Var(uint n) { while (n >= 128) { writer.Write((byte)((n & 127) | 128)); n >>= 7; } writer.Write((byte)n); }
        Var(actor); Var(0); writer.Write(skill); writer.Write((byte)1); writer.Write((byte)2); Var(target);
        writer.Write(0f); writer.Write(1f); writer.Write(2f); writer.Write(3f);
        var events = new List<GameEvent>(); var parser = new PacketParser(GameData.Empty, events.Add); parser.Handle(Opcodes.Cast, stream.ToArray());
        if (!valid) { Assert.Empty(events); return; }
        var cast = Assert.IsType<CastEvent>(Assert.Single(events)); Assert.Equal(actor, cast.ActorId); Assert.Equal(target, cast.TargetId); Assert.Equal(skill, cast.SkillCode);
    }
    [Fact]
    public void A_zero_target_does_not_start_a_nearby_boss_but_updates_an_engaged_one()
    {
        var data = GameData.Empty; data.Npcs[2300171] = new(2300171, "Ultimate Berk", true, false); data.Skills[1800030] = "Rush";
        var engine = Engine(); var adapter = new BossLiveAdapter(data, engine, (id, _) => id == 100, () => null);
        adapter.Process(new NpcSeenEvent(1000, 700, 2300171)); adapter.Process(new CastEvent(2000, 700, 0, 1800030)); Assert.Empty(engine.Snapshot(2000).Fights);
        adapter.Process(new DamageEvent(3000, 100, 700, 11020000, 1, HitFlags.None)); adapter.Process(new CastEvent(4000, 700, 0, 1800030));
        Assert.Equal("Rush", Assert.Single(engine.Snapshot(4000).ObservedSkills).Name);
    }
}
