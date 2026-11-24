using AionMeter.Core.Bosses;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public class BossAssistantTests
{
    private static BossProfile Profile(params BossMechanic[] mechanics) => new() { Id = "synthetic", Name = "TEST Boss", NpcCodes = [-9001], Synthetic = true, Evidence = "Synthetic", ResetAfterSeconds = 120, Mechanics = mechanics };
    private static BossMechanic Rule => new() { Id = "spin", SkillId = -101, Name = "TEST Obrót", FirstSeconds = 20, CooldownSeconds = 30, Confidence = 1 };
    private static BossSignal Signal(long at, BossSignalKind kind, int skill = 0, uint actor = 10) => new(at, kind, actor, -9001)
    { BossName = "TEST Boss", SkillId = skill, SkillName = "TEST Obrót", Synthetic = true, PullConfirmed = true };
    private static BossAssistantEngine Engine(params BossMechanic[] rules) => new(new([Profile(rules.Length == 0 ? [Rule] : rules)]), true);
    [Fact]
    public void Pull_timer_and_observed_cast_resynchronize_the_next_occurrence()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull));
        Assert.Equal(20, Assert.Single(engine.Snapshot(1000).Bars).Seconds);
        engine.Process(Signal(20000, BossSignalKind.Cast, -101));
        Assert.Equal(30, Assert.Single(engine.Snapshot(20000).Bars).Seconds);
        Assert.False(Assert.Single(engine.Snapshot(20000).Alerts).Predicted);
    }
    [Fact]
    public void An_unconfirmed_midfight_start_does_not_start_fixed_pull_timers()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull) with { PullConfirmed = false });
        Assert.Empty(engine.Snapshot(1000).Bars);
        engine.Process(Signal(5000, BossSignalKind.Cast, -101)); Assert.Equal(30, Assert.Single(engine.Snapshot(5000).Bars).Seconds);
    }
    [Fact]
    public void Aoe_duplicate_casts_do_not_repeat_alerts_or_extend_the_cooldown()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Process(Signal(2000, BossSignalKind.Cast, -101)); engine.Process(Signal(2100, BossSignalKind.Cast, -101));
        Assert.Single(engine.TakeAlerts()); Assert.Equal(32000, Assert.Single(engine.Snapshot(2200).Bars).DueAt);
    }
    [Fact]
    public void Warning_thresholds_fire_once_and_a_delayed_tick_never_plays_three_at_once()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Tick(18000);
        Assert.Single(engine.TakeAlerts()); engine.Tick(18100); Assert.Empty(engine.TakeAlerts());
        engine.Tick(30000); Assert.Empty(engine.Snapshot(30000).Bars); Assert.Empty(engine.TakeAlerts());
    }
    [Fact]
    public void No_new_cooldown_is_invented_when_a_prediction_expires_without_a_cast()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Snapshot(28000);
        Assert.Empty(engine.Snapshot(31000).Bars);
    }
    [Theory]
    [InlineData(BossSignalKind.Death)] [InlineData(BossSignalKind.Reset)]
    public void Fight_end_clears_timers_and_queued_alerts(BossSignalKind end)
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Tick(12000); engine.Process(Signal(13000, end));
        var state = engine.Snapshot(14000); Assert.Empty(state.Fights); Assert.Empty(state.Bars); Assert.Empty(engine.TakeAlerts());
    }
    [Fact]
    public void Phase_uses_valid_hp_thresholds_and_cancels_old_predictions()
    {
        var engine = Engine(Rule, new() { Id = "phase", Name = "TEST Faza 2", HpBelowPercent = 70, SetPhase = 2 },
            new() { Id = "phase-spin", Name = "TEST Faza 2 atak", SkillId = -102, Phase = 2, FirstSeconds = 8 });
        engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Process(Signal(2000, BossSignalKind.Hp) with { HpPercent = 69 });
        var state = engine.Snapshot(2000); Assert.Equal(2, state.Fights[0].Phase); Assert.Equal(-102, Assert.Single(state.Bars).SkillId);
        engine.Process(Signal(2200, BossSignalKind.Hp) with { HpPercent = 68 }); Assert.Equal(10000, Assert.Single(engine.Snapshot(2200).Bars).DueAt);
    }
    [Fact]
    public void Boss_hp_reset_does_not_leave_old_timers()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull));
        engine.Process(Signal(2000, BossSignalKind.Hp) with { HpPercent = 50 }); engine.Process(Signal(3000, BossSignalKind.Hp) with { HpPercent = 100 });
        Assert.Empty(engine.Snapshot(3000).Fights);
    }
    [Fact]
    public void Sequence_is_anchored_to_an_observed_skill_and_does_not_duplicate_warnings_after_reanchoring()
    {
        var engine = Engine(new BossMechanic { Id = "combo", SkillId = -102, Name = "TEST Fala", AfterSkillId = -101, AfterSeconds = 12 });
        engine.Process(Signal(1000, BossSignalKind.Pull)); Assert.Empty(engine.Snapshot(1000).Bars);
        engine.Process(Signal(2000, BossSignalKind.Cast, -101)); engine.TakeAlerts(); engine.Tick(5000); Assert.Single(engine.TakeAlerts());
        engine.Process(Signal(7000, BossSignalKind.Cast, -101)); engine.TakeAlerts(); engine.Tick(9000); Assert.Empty(engine.TakeAlerts());
        Assert.Equal(19000, Assert.Single(engine.Snapshot(9000).Bars).DueAt);
    }
    [Fact]
    public void Demo_data_cannot_enter_live_engine_and_roles_filter_mechanics()
    {
        var live = new BossAssistantEngine(new([Profile(Rule)])); live.Process(Signal(1000, BossSignalKind.Pull)); Assert.Empty(live.Snapshot(1000).Fights);
        var engine = Engine(Rule with { Roles = ["tank"] }); engine.Process(Signal(1000, BossSignalKind.Pull)); Assert.Empty(engine.Snapshot(1000).Bars);
    }
    [Fact]
    public void Disabled_mechanic_is_scoped_to_its_profile_and_removed_immediately()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull));
        engine.Configure(new() { DisabledMechanics = new HashSet<string> { "synthetic.spin" } }); Assert.Empty(engine.Snapshot(1100).Bars);
    }
    [Fact]
    public void Two_bosses_have_independent_encounters_and_timers()
    {
        var engine = Engine(); engine.Process(Signal(1000, BossSignalKind.Pull)); engine.Process(Signal(2000, BossSignalKind.Pull, actor: 20));
        engine.Process(Signal(3000, BossSignalKind.Death)); Assert.Equal(20u, Assert.Single(engine.Snapshot(3000).Fights).Actor);
        Assert.Equal(20u, Assert.Single(engine.Snapshot(3000).Bars).Actor);
    }
    [Fact]
    public void Profile_validation_rejects_wrong_versions_invalid_timings_and_live_synthetic_ids()
    {
        Assert.Null(BossProfileCatalog.Validate(Profile(Rule)));
        Assert.NotNull(BossProfileCatalog.Validate(Profile(Rule) with { SchemaVersion = 2 }));
        Assert.NotNull(BossProfileCatalog.Validate(Profile(Rule) with { Synthetic = false }));
        Assert.NotNull(BossProfileCatalog.Validate(Profile(Rule with { CooldownSeconds = double.NaN })));
        Assert.NotNull(BossProfileCatalog.Validate(Profile(Rule with { AfterSkillId = -1 })));
    }
    [Fact]
    public void Dynamic_analysis_requires_multiple_fights_and_stable_real_cast_intervals()
    {
        var rows = new List<BossLogRecord>();
        for (var f = 0; f < 2; f++) { var id = Guid.NewGuid(); for (var i = 0; i < 5; i++) rows.Add(new(1, id, new(i * 10000, BossSignalKind.Cast, 1, 2300001) { SkillId = 1800001, SkillName = "Example" })); }
        var proposal = Assert.Single(BossFightAnalyzer.Analyze(rows)); Assert.True(proposal.Eligible); Assert.Equal(10, proposal.MedianSeconds); Assert.Equal(8, proposal.Samples);
        Assert.False(Assert.Single(BossFightAnalyzer.Analyze(rows.Where(r => r.SessionId == rows[0].SessionId))).Eligible);
        Assert.Empty(BossFightAnalyzer.Analyze(rows.Select(r => r with { Signal = r.Signal with { Synthetic = true } })));
    }
    [Fact]
    public void Replay_uses_virtual_time_and_clears_on_reset()
    {
        var engine = Engine(); var replay = new BossReplay(engine, [Signal(1000, BossSignalKind.Pull), Signal(20000, BossSignalKind.Cast, -101), Signal(25000, BossSignalKind.Reset)]);
        replay.AdvanceTo(12000); Assert.Equal(9, Assert.Single(engine.Snapshot(12000).Bars).Seconds);
        replay.AdvanceTo(21000); Assert.Equal(29, Assert.Single(engine.Snapshot(21000).Bars).Seconds);
        replay.AdvanceTo(30000); Assert.Empty(engine.Snapshot(30000).Bars); Assert.True(replay.Complete);
    }
}
