using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using Xunit;

namespace Aurum.Tests;

public class HealingTests
{
    private static CombatTracker Setup()
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
        tracker.Process(new SelfIdentifiedEvent(0, 100, "Tank", 2314, GameClass.Templar));
        tracker.Process(new PlayerSeenEvent(0, 101, "Healer", 2314, GameClass.Cleric));
        tracker.Process(new PlayerSeenEvent(0, 102, "Chanter", 2314, GameClass.Chanter));
        tracker.Process(new NpcSeenEvent(0, 500, 2000002, 500000));
        tracker.Process(new DamageEvent(1000, 100, 500, 12010000, 10000, HitFlags.None));
        return tracker;
    }

    [Fact]
    public void A_healer_with_no_damage_is_listed_and_preserves_damage_totals()
    {
        var tracker = Setup();
        tracker.Process(new HealEvent(1500, 101, 100, 17010000, 5000, HitFlags.None));
        tracker.Process(new DamageEvent(3000, 100, 500, 12010000, 10000, HitFlags.None));
        var snapshot = tracker.Snapshot(null, 3000)!;
        var healer = snapshot.Combatants.Single(p => p.ActorId == 101);
        Assert.Equal(0, healer.Damage);
        Assert.Equal(5000, healer.Healing);
        Assert.Equal(20000, snapshot.TotalDamage);
        Assert.Equal(5000, snapshot.TotalHealing);
        Assert.Equal(2500, healer.Healing / (snapshot.CombatMs / 1000.0));
    }

    [Fact]
    public void Direct_and_periodic_heals_are_attributed_by_skill_and_player()
    {
        var tracker = Setup();
        tracker.Process(new HealEvent(1200, 101, 100, 17010000, 3000, HitFlags.Critical));
        tracker.Process(new HealEvent(1300, 101, 102, 17010000, 2000, HitFlags.Dot));
        tracker.Process(new HealEvent(1400, 102, 100, 18010000, 1000, HitFlags.Dot));
        var snapshot = tracker.Snapshot(null, 1500)!;
        var skill = Assert.Single(snapshot.Combatants.Single(p => p.ActorId == 101).HealingSkills);
        Assert.Equal(5000, skill.Amount); Assert.Equal(2, skill.Events);
        Assert.Equal(6000, snapshot.TotalHealing);
    }

    [Fact]
    public void Npc_healing_negative_values_and_heals_outside_fights_are_ignored()
    {
        var tracker = Setup();
        tracker.Process(new HealEvent(1200, 500, 500, 17010000, 9000, HitFlags.None));
        tracker.Process(new HealEvent(1300, 101, 100, 17010000, -5000, HitFlags.None));
        Assert.Equal(0, tracker.Snapshot(null, 1500)!.TotalHealing);
        tracker.Reset();
        tracker.Process(new HealEvent(2000, 101, 100, 17010000, 1000, HitFlags.None));
        Assert.Null(tracker.Snapshot(null, 2500));
    }

    [Fact]
    public void Summon_healing_is_credited_to_its_known_owner()
    {
        var tracker = Setup();
        tracker.Process(new SummonSeenEvent(1100, 700, 101, null, 2920001));
        tracker.Process(new HealEvent(1200, 700, 100, 17010000, 4500, HitFlags.None));
        var snapshot = tracker.Snapshot(null, 1500)!;
        Assert.Equal(4500, snapshot.Combatants.Single(p => p.ActorId == 101).Healing);
        Assert.DoesNotContain(snapshot.Combatants, p => p.ActorId == 700);
    }

    [Fact]
    public void New_encounter_resets_healing_and_json_preserves_saved_results()
    {
        var tracker = Setup();
        tracker.Process(new HealEvent(1200, 101, 100, 17010000, 1500, HitFlags.None));
        var original = tracker.Snapshot(null, 2000)!;
        var restored = JsonSerializer.Deserialize<EncounterSnapshot>(JsonSerializer.Serialize(original))!;
        Assert.Equal(1500, restored.TotalHealing);
        Assert.Equal(1500, Assert.Single(restored.Combatants.Single(p => p.ActorId == 101).HealingSkills).Amount);
        tracker.Reset();
        tracker.Process(new DamageEvent(3000, 100, 500, 12010000, 10000, HitFlags.None));
        Assert.Equal(0, tracker.Snapshot(null, 3500)!.TotalHealing);
    }

    [Fact]
    public void A_late_player_name_updates_the_pure_healers_row()
    {
        var tracker = Setup();
        tracker.Process(new HealEvent(1200, 777, 100, 17010000, 900, HitFlags.None));
        tracker.Process(new PlayerSeenEvent(1300, 777, "LateHealer", 2314, GameClass.Cleric));
        var healer = tracker.Snapshot(null, 1500)!.Combatants.Single(p => p.ActorId == 777);
        Assert.Equal("LateHealer", healer.Name); Assert.Equal(900, healer.Healing);
    }
}
