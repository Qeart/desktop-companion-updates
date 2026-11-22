using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Storage;

namespace Aurum.Tests;

public class NamePersistenceTests
{
    [Fact]
    public void Names_survive_a_disk_round_trip_and_meter_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "aurum-names-" + Guid.NewGuid() + ".json");
        try
        {
            var first = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
            first.Process(new SelfIdentifiedEvent(0, 100, "MyHealer", 2314, GameClass.Cleric));
            first.Process(new PlayerSeenEvent(0, 101, "NamedTank", 2314, GameClass.Templar));
            new NameCache(path).Save(first.ExportCache());
            var restored = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
            restored.ImportCache(new NameCache(path).Load()!);
            restored.Process(new DamageEvent(1000, 101, 500, 12010000, 1000, HitFlags.None));
            restored.Process(new HealEvent(1100, 100, 101, 17010000, 500, HitFlags.None));
            var snapshot = restored.Snapshot(null, 1200)!;
            Assert.Equal("NamedTank", snapshot.Combatants.Single(c => c.ActorId == 101).Name);
            Assert.Equal("MyHealer", snapshot.Combatants.Single(c => c.ActorId == 100).Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Unknown_number_labels_are_not_saved_as_real_names()
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All });
        tracker.Process(new DamageEvent(1000, 101, 500, 12010000, 1000, HitFlags.None));
        Assert.StartsWith("#", tracker.Snapshot(null, 1200)!.Combatants.Single().Name);
        Assert.Empty(tracker.ExportCache().Players);
        tracker.Process(new PlayerSeenEvent(1300, 101, "RealName", 2314, GameClass.Templar));
        Assert.Equal("RealName", tracker.Snapshot(null, 1400)!.Combatants.Single().Name);
        Assert.Equal("RealName", tracker.ExportCache().Players.Single().Name);
    }
}
