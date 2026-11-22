using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public sealed class DungeonScopeTests
{
    private static CombatTracker Tracker(bool roster)
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { GroupOnly = true, TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.Process(new SelfIdentifiedEvent(100, 100, "Self", 2305, GameClass.Chanter));
        tracker.Process(new ZoneChangedEvent(200, 610052, "Test dungeon", true));
        if (roster) tracker.Process(new PartyRosterEvent(250,
            [new("Self", 2305, 45, 0, 150000, GameClass.Chanter), new("Friend", 2308, 45, 0, 150000, GameClass.Gladiator)], 55));
        return tracker;
    }
    private static void Name(CombatTracker tracker, uint id, string name, int server = 0)
        => tracker.Process(new PlayerSeenEvent(300, id, name, server, GameClass.Gladiator));
    private static void Hit(CombatTracker tracker, uint id, long amount = 1000, long at = 1000)
        => tracker.Process(new DamageEvent(at, id, 500, 11020000, amount, HitFlags.None));
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Dungeon_players_without_server_ids_contribute_to_damage_and_healing(bool roster)
    {
        var tracker = Tracker(roster); Name(tracker, 101, "Friend");
        Hit(tracker, 100); Hit(tracker, 101, 3000);
        tracker.Process(new HealEvent(1500, 101, 100, 17010000, 2000, HitFlags.None));
        var snapshot = tracker.Snapshot(null, 2000)!;
        Assert.Equal(4000, snapshot.TotalDamage); Assert.Equal(2000, snapshot.TotalHealing); Assert.Equal(2, snapshot.Combatants.Count);
        Assert.Equal(.75, snapshot.Combatants.Single(p => p.ActorId == 101).Share);
        Assert.NotNull(tracker.Detail(null, 101)); Assert.Equal(2, tracker.Record(null)!.Summary.Combatants.Count);
    }
    [Fact]
    public void Restart_inside_dungeon_uses_current_instance_name_cache_without_a_new_roster()
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { GroupOnly = true, TargetMode = TargetMode.All });
        tracker.ImportCache(new SessionState(100, 610052,
            [new(100, "Self", GameClass.Chanter, 2305), new(101, "Friend", GameClass.Gladiator, 0)], []));
        Hit(tracker, 101); Assert.Equal(101u, Assert.Single(tracker.Snapshot(null, 2000)!.Combatants).ActorId);
    }
    [Fact]
    public void All_dungeon_participants_are_included_even_if_only_a_subparty_roster_arrives()
    {
        var tracker = Tracker(true); Name(tracker, 101, "Friend"); Name(tracker, 102, "Other participant");
        Hit(tracker, 100); Hit(tracker, 101); Hit(tracker, 102);
        Assert.Equal(3, tracker.Snapshot(null, 2000)!.Combatants.Count);
    }
    [Fact]
    public void Dungeon_npcs_and_unowned_effects_do_not_become_player_rows()
    {
        var tracker = Tracker(false); Hit(tracker, 100);
        tracker.Process(new NpcSeenEvent(1100, 102, 2000001, 1000, "NPC"));
        tracker.Process(new SummonSeenEvent(1100, 701, 0, null, 2920011));
        Hit(tracker, 102, 90000, 1200); Hit(tracker, 701, 90000, 1250); Hit(tracker, 103, 90000, 1300);
        var snapshot = tracker.Snapshot(null, 2000)!;
        Assert.Equal(1000, snapshot.TotalDamage); Assert.Equal(100u, Assert.Single(snapshot.Combatants).ActorId);
    }
    [Fact]
    public void Instance_summon_damage_is_attributed_to_its_named_owner()
    {
        var tracker = Tracker(false); Name(tracker, 101, "Friend");
        tracker.Process(new SummonSeenEvent(500, 701, 101, null, 2920011)); Hit(tracker, 701);
        Assert.Equal(101u, Assert.Single(tracker.Snapshot(null, 2000)!.Combatants).ActorId);
    }
    [Fact]
    public void Leaving_the_dungeon_restores_the_strict_party_filter()
    {
        var tracker = Tracker(false); Name(tracker, 101, "Friend"); Hit(tracker, 101);
        tracker.Process(new ZoneChangedEvent(3000, 1110, "Open world", false));
        Name(tracker, 101, "Friend"); Hit(tracker, 101, 90000, 4000);
        var finished = tracker.Snapshot(null, 4500)!;
        Assert.False(finished.IsActive); Assert.Equal(1000, finished.TotalDamage); Assert.False(tracker.InDungeon);
        Hit(tracker, 100, 1, 5000); Assert.Equal(1, tracker.Snapshot(null, 5500)!.TotalDamage);
    }
}
