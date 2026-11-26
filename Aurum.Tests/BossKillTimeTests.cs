using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public class BossKillTimeTests
{
    [Fact]
    public void Constant_HP_loss_produces_a_remaining_time_after_a_short_warmup()
    {
        var estimator = new BossKillTimeEstimator(); estimator.Observe(1000, 10000); estimator.Observe(2000, 9000);
        Assert.Null(estimator.Estimate(2000)); estimator.Observe(4000, 7000);
        var result = estimator.Estimate(4000)!; Assert.Equal(7, result.Seconds); Assert.Equal(1000, result.HpPerSecond); Assert.Equal(3, result.SampleSeconds);
    }
    [Fact]
    public void High_frequency_updates_use_a_bounded_recent_window()
    {
        var estimator = new BossKillTimeEstimator();
        for (var at = 1000; at <= 31000; at += 20) estimator.Observe(at, 200000 - (at - 1000) * 5);
        var result = estimator.Estimate(31000)!;
        Assert.Equal(10, result.Seconds, 4); Assert.InRange(result.SampleSeconds, 9.9, 10.1);
    }
    [Fact]
    public void Recent_DPS_changes_replace_old_fight_average()
    {
        var estimator = new BossKillTimeEstimator();
        for (var second = 0; second <= 21; second++) estimator.Observe(1000 + second * 1000, second <= 10 ? 200000 - second * 1000 : 190000 - (second - 10) * 2000);
        Assert.Equal(84, estimator.Estimate(22000)!.Seconds, 4);
    }
    [Fact]
    public void Healing_or_missing_updates_requires_new_reliable_samples()
    {
        var estimator = new BossKillTimeEstimator(); estimator.Observe(1000, 10000); estimator.Observe(4000, 7000);
        Assert.NotNull(estimator.Estimate(4000)); Assert.Null(estimator.Estimate(7001));
        estimator.Observe(8000, 6000); Assert.Null(estimator.Estimate(8000));
        estimator.Observe(11000, 5000); Assert.NotNull(estimator.Estimate(11000));
        estimator.Observe(12000, 9000); Assert.Null(estimator.Estimate(12000));
    }
    [Fact]
    public void A_confirmed_plateau_hides_the_old_estimate()
    {
        var estimator = new BossKillTimeEstimator(); estimator.Observe(1000, 10000); estimator.Observe(4000, 7000);
        for (var at = 5000; at <= 8000; at += 1000) estimator.Observe(at, 7000);
        Assert.Null(estimator.Estimate(8000));
    }
    [Fact]
    public void Out_of_order_or_invalid_samples_cannot_replace_newer_HP()
    {
        var estimator = new BossKillTimeEstimator(); estimator.Observe(1000, 10000); estimator.Observe(4000, 7000);
        estimator.Observe(3500, 9000); estimator.Observe(4000, 5000); estimator.Observe(5000, -1);
        Assert.Equal(7, estimator.Estimate(4000)!.Seconds); Assert.Null(estimator.Estimate(3999));
        estimator.Observe(5000, 0); Assert.Null(estimator.Estimate(5000));
    }
    [Fact]
    public void No_HP_loss_or_death_never_produces_a_countdown()
    {
        var estimator = new BossKillTimeEstimator(); estimator.Observe(1000, 10000); estimator.Observe(4000, 10000);
        Assert.Null(estimator.Estimate(4000)); estimator.Clear(); Assert.Null(estimator.Estimate(4000));
    }
    [Fact]
    public void Add_damage_does_not_change_boss_ETA_and_history_does_not_replay_a_live_estimate()
    {
        var data = GameData.Empty; data.Npcs[2000001] = new(2000001, "Fixture Boss", true, false);
        var tracker = new CombatTracker(data, new MeterOptions { TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.Process(new SelfIdentifiedEvent(100, 100, "Self", 2305, GameClass.Gladiator));
        tracker.Process(new NpcSeenEvent(500, 500, 2000001, 10000));
        tracker.Process(new DamageEvent(1000, 100, 500, 11020000, 1000, HitFlags.None));
        tracker.Process(new NpcHpEvent(1000, 500, 9000, 10000));
        tracker.Process(new DamageEvent(3900, 100, 501, 11020000, 1000000, HitFlags.None));
        tracker.Process(new NpcHpEvent(4000, 500, 6000, 10000));
        var snapshot = tracker.Snapshot(null, 4000)!;
        Assert.Equal(6, snapshot.KillEstimate!.Seconds); Assert.True(snapshot.PartyDps > 100000);
        Assert.DoesNotContain("KillEstimate", JsonSerializer.Serialize(snapshot));
        Assert.Null(tracker.Snapshot(null, 7001)!.KillEstimate);
        tracker.Process(new DeathEvent(7500, 500)); Assert.Null(tracker.Snapshot(null, 7600)!.KillEstimate);
    }
}
