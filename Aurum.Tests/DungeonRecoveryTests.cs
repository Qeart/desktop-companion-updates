using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace Aurum.Tests;

public class DungeonRecoveryTests
{
    private static GameData Data()
    {
        var data = GameData.Empty;
        data.Npcs[2320171] = new(2320171, "Ultimate Berk", true, false, 600004);
        data.Npcs[2000001] = new(2000001, "World mob", false, false);
        data.Maps[600004] = "Krao Cave Hard";
        return data;
    }
    private static CombatTracker Tracker(GameData? data = null)
    {
        var tracker = new CombatTracker(data ?? Data(), new MeterOptions { GroupOnly = true, TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.Process(new ZoneChangedEvent(100, 1110, "World", false));
        tracker.Process(new SelfIdentifiedEvent(200, 100, "Self", 2305, GameClass.Chanter));
        tracker.Process(new PlayerSeenEvent(300, 101, "Teammate", 0, GameClass.Gladiator));
        return tracker;
    }
    private static void Hit(CombatTracker tracker, uint source = 101, uint target = 500, long at = 1000)
        => tracker.Process(new DamageEvent(at, source, target, 11020000, 1000, HitFlags.None));
    private static JsonElement Group(CombatTracker tracker) => JsonSerializer.SerializeToElement(tracker.GroupState);

    [Fact]
    public void Hard_Berk_with_stale_world_map_accepts_fresh_named_participants_without_server_or_roster()
    {
        var tracker = Tracker(); tracker.Process(new NpcSeenEvent(500, 500, 2320171, 100000));
        Hit(tracker); Hit(tracker, 100, at: 1100);
        tracker.Process(new HealEvent(1200, 101, 100, 17010000, 400, HitFlags.None));
        tracker.Process(new SummonSeenEvent(1300, 701, 101, null, 2920011)); Hit(tracker, 701, at: 1400);
        var state = tracker.Snapshot(null, 1500)!;
        Assert.Equal(3000, state.TotalDamage); Assert.Equal(400, state.TotalHealing);
        Assert.Equal(2, state.Combatants.Count); Assert.Equal("Krao Cave Hard", state.Zone);
        Assert.True(tracker.InDungeon); Assert.True(tracker.IsCurrentGroupActor(101, 1500));
        var diagnostic = Group(tracker);
        Assert.Equal(1110, diagnostic.GetProperty("MapId").GetInt32());
        Assert.Equal(600004, diagnostic.GetProperty("CombatDungeonMap").GetInt32());
        Assert.Equal("VerifiedDungeonTarget", diagnostic.GetProperty("DungeonEvidence").GetString());
        Assert.Equal(1110, tracker.ExportCache().MapId); // enemy metadata never moves the player's GPS
    }
    [Fact]
    public void Seeing_an_instance_boss_does_not_open_world_filtering_or_create_an_encounter()
    {
        var tracker = Tracker(); tracker.Process(new NpcSeenEvent(500, 500, 2320171, 100000));
        Assert.False(tracker.InDungeon); Hit(tracker, target: 600);
        Assert.Null(tracker.Snapshot(null, 1200)); Assert.False(tracker.InDungeon);
        Assert.Single(Group(tracker).GetProperty("RejectedActors").EnumerateArray());
    }
    [Fact]
    public void World_filtering_resumes_after_the_boss_dies()
    {
        var tracker = Tracker(); tracker.Process(new NpcSeenEvent(500, 500, 2320171, 100000)); Hit(tracker);
        tracker.Process(new DeathEvent(1500, 500)); tracker.Reset();
        Assert.False(tracker.InDungeon); Hit(tracker, target: 600, at: 2000);
        Assert.Null(tracker.Snapshot(null, 2100)); Hit(tracker, 100, 600, 2200);
        Assert.Equal(100u, Assert.Single(tracker.Snapshot(null, 2300)!.Combatants).ActorId);
    }
    [Theory]
    [InlineData("anonymous")] [InlineData("unowned")] [InlineData("cachedBoss")] [InlineData("cachedPlayer")]
    public void Fallback_context_cannot_promote_unverified_entities(string kind)
    {
        var tracker = Tracker(); uint source = 101;
        if (kind == "cachedBoss") tracker.ImportCache(new(null, 1110, [], [new(500, 2320171, 100000)]));
        else tracker.Process(new NpcSeenEvent(500, 500, 2320171, 100000));
        if (kind == "anonymous") source = 999;
        if (kind == "unowned") { source = 701; tracker.Process(new SummonSeenEvent(700, 701, 0, null, 2920011)); }
        if (kind == "cachedPlayer") { source = 102; tracker.ImportCache(new(null, 1110, [new(102, "OldName", GameClass.Gladiator, 0)], [])); }
        Hit(tracker, source); Assert.Null(tracker.Snapshot(null, 1200)); Assert.False(tracker.InDungeon);
    }
    [Fact]
    public void Instance_timer_recovers_scope_before_boss_contact_and_expires_without_packets()
    {
        var tracker = Tracker(); tracker.Process(new InstanceTimerEvent(500, 600004, 2, 400, 2000));
        Assert.True(tracker.InDungeon); Assert.Equal("InstanceTimer", Group(tracker).GetProperty("DungeonEvidence").GetString());
        tracker.Tick(2100); Assert.False(tracker.InDungeon); Hit(tracker, at: 2200);
        Assert.Null(tracker.Snapshot(null, 2300));
    }
    [Fact]
    public void Authoritative_instance_timer_allows_teammate_damage_on_trash_before_a_boss_is_seen()
    {
        var tracker = Tracker(); tracker.Process(new InstanceTimerEvent(500, 600004, 2, 400, 60000)); Hit(tracker);
        Assert.Equal(101u, Assert.Single(tracker.Snapshot(null, 1200)!.Combatants).ActorId);
        Assert.Equal("Krao Cave Hard", tracker.Snapshot(null, 1200)!.Zone);
    }
    [Theory]
    [InlineData("zone")] [InlineData("identity")]
    public void Transition_clears_timer_and_boss_fallback_context(string transition)
    {
        var tracker = Tracker(); tracker.Process(new InstanceTimerEvent(400, 600004, 2, 400, 60000));
        tracker.Process(new NpcSeenEvent(500, 500, 2320171, 100000)); Hit(tracker);
        tracker.Process(transition == "zone" ? new ZoneChangedEvent(2000, 2010, "World", false) : new LocalIdentityLostEvent(2000));
        Assert.False(tracker.InDungeon);
    }
    [Theory]
    [InlineData(0)] [InlineData(123)] [InlineData(500000)] [InlineData(10000000)]
    public void Implausible_current_zone_records_are_ignored(uint map)
    {
        var rows = new List<GameEvent>(); var parser = new PacketParser(Data(), rows.Add);
        parser.Handle(Opcodes.CurrentZone, BitConverter.GetBytes(map)); Assert.Empty(rows);
        parser.Handle(Opcodes.CurrentZone, [1, 2, 3]); Assert.Empty(rows);
    }
    [Fact]
    public void Both_zone_layouts_share_teleport_detection_without_changing_legacy_offset()
    {
        var rows = new List<GameEvent>(); var parser = new PacketParser(Data(), rows.Add);
        parser.Handle(Opcodes.MapLoad, BitConverter.GetBytes(1u).Concat(BitConverter.GetBytes(1110u)).ToArray());
        parser.Handle(Opcodes.CurrentZone, BitConverter.GetBytes(600004u));
        parser.Handle(Opcodes.MapLoad, BitConverter.GetBytes(2u).Concat(BitConverter.GetBytes(600004u)).ToArray());
        var zones = rows.Cast<ZoneChangedEvent>().ToArray(); Assert.Equal(new[] { 1110, 600004, 600004 }, zones.Select(z => z.MapId));
        Assert.False(zones[0].IsTeleport); Assert.False(zones[1].IsTeleport); Assert.True(zones[2].IsTeleport);
    }
    [Fact]
    public void Shipped_NPC_metadata_retains_the_Hard_instance_ID()
    {
        var data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "boss-data"));
        Assert.Equal(600004, data.Npcs[2320171].DungeonId); Assert.Equal(600001, data.Npcs[2300171].DungeonId);
    }
}
