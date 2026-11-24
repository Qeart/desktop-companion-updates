using System.Text.Json;
using AionMeter.Core.Bosses;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public class BerkMechanicTests
{
    private sealed record Cast(long At, int SkillId);
    private static BossProfileCatalog Catalog => BossProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "boss-data", "bossmods"));
    private static BossSignal S(long at, BossSignalKind kind, int skill = 0) => new(at, kind, 700, 2320171)
        { BossName = "Ultimate Berk", MapId = 1110, SkillId = skill, SkillName = "Skill " + skill };
    [Fact]
    public void Hard_fight_replay_warns_for_every_reported_spin_and_before_each_Wave()
    {
        // Relative NPC timings only; no player names/IDs, epochs, session IDs or network data.
        var rows = JsonSerializer.Deserialize<Cast[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "berk-hard-casts.json")), BossJson.Options)!;
        var engine = new BossAssistantEngine(Catalog); engine.Process(S(1000, BossSignalKind.Pull));
        var spins = 0; var waves = 0;
        foreach (var row in rows) {
            var at = 1000 + row.At;
            engine.Process(S(at, BossSignalKind.Cast, row.SkillId)); var state = engine.Snapshot(at);
            if (row.SkillId == 1800170) {
                spins++; Assert.Contains(state.Alerts, a => !a.Predicted && a.Text.Contains("Możliwy obrót") && a.At == at);
                Assert.DoesNotContain(state.Bars, b => b.SkillId == 1800170); // no invented spin cooldown
            }
            if (row.SkillId == 1800150) {
                waves++; var bar = Assert.Single(state.Bars, b => b.SkillId == 1800053);
                Assert.Equal("windup", bar.Kind); Assert.Equal(8.4, bar.Seconds, 3); Assert.Equal(0, bar.Fraction);
                Assert.Contains(state.Alerts, a => a.Predicted && a.Text.Contains("skałą") && a.At == at);
                var halfway = engine.Snapshot(at + 4200).Bars.Single(b => b.SkillId == 1800053);
                Assert.Equal(.5, halfway.Fraction, 3); Assert.Equal(4.2, halfway.Seconds, 3);
            }
            if (row.SkillId == 1800053) Assert.DoesNotContain(state.Bars, b => b.SkillId == 1800053);
        }
        Assert.Equal(3, spins); Assert.Equal(2, waves);
    }
    [Fact]
    public void Spin_warning_survives_attack_ticks_and_expires_without_another_packet()
    {
        var engine = new BossAssistantEngine(Catalog); var records = new List<BossLogRecord>(); engine.Record += records.Add;
        engine.Process(S(1000, BossSignalKind.Pull)); engine.Process(S(2000, BossSignalKind.Cast, 1800170));
        for (var i = 0; i < 7; i++) engine.Process(S(2100 + i * 800, BossSignalKind.Cast, 1800160));
        var state = engine.Snapshot(8100);
        Assert.Single(state.Alerts); Assert.Contains("obrót", state.Alerts[0].Text);
        Assert.DoesNotContain(state.ObservedSkills, s => s.SkillId == 1800160);
        Assert.Equal(7, records.Count(r => r.Signal.SkillId == 1800160)); // quiet is presentation only
        Assert.Empty(engine.Snapshot(9001).Alerts);
    }
    [Fact]
    public void Known_casts_provide_reactions_without_guessed_cast_lengths()
    {
        var engine = new BossAssistantEngine(Catalog); engine.Process(S(1000, BossSignalKind.Pull));
        engine.Process(S(2000, BossSignalKind.Cast, 1800030)); Assert.Contains(engine.TakeAlerts(), a => a.Text.Contains("linii"));
        engine.Process(S(4000, BossSignalKind.Cast, 1800070)); Assert.Contains(engine.TakeAlerts(), a => a.Text.Contains("uważaj na obrót"));
        Assert.Empty(engine.Snapshot(4000).Bars);
        Assert.All(Catalog.Find(2320171, 1110, false)!.Mechanics, m => Assert.Null(m.CastSeconds));
    }
    [Fact]
    public void Disabling_Wave_or_community_rules_also_disables_its_preparation_warning()
    {
        foreach (var options in new[] { new BossEngineOptions { CommunityTimers = false }, new BossEngineOptions { DisabledMechanics = new HashSet<string> { "boss-2300171.skill-1800053" } } }) {
            var engine = new BossAssistantEngine(Catalog); engine.Configure(options); engine.Process(S(1000, BossSignalKind.Pull));
            engine.Process(S(2000, BossSignalKind.Cast, 1800150)); Assert.Empty(engine.Snapshot(2000).Bars); Assert.Empty(engine.TakeAlerts());
        }
    }
    [Fact]
    public void Unmapped_NPC_cast_is_journalled_separately_and_cannot_trigger_a_boss_rule()
    {
        var data = GameData.Empty; data.Npcs[2320171] = new(2320171, "Ultimate Berk", true, false);
        // A server-announced helper need not already exist in the static NPC catalog.
        var engine = new BossAssistantEngine(Catalog); var records = new List<BossLogRecord>(); engine.Record += records.Add;
        var adapter = new BossLiveAdapter(data, engine, (id, _) => id == 100, () => new BossSnapshot(700, 2320171, "Ultimate Berk", 100, 100));
        adapter.Process(new NpcSeenEvent(1000, 700, 2320171)); adapter.Process(new NpcSeenEvent(1100, 800, 2000001));
        adapter.Process(new DamageEvent(1200, 100, 700, 11020000, 1, HitFlags.None));
        adapter.Process(new CastEvent(2000, 800, 0, 1800150)); adapter.Process(new CastEvent(2100, 800, 0, 1800150));
        var row = Assert.Single(records, r => r.Signal.Kind == BossSignalKind.UnmappedCast);
        Assert.Equal(800u, row.Signal.SourceActor); Assert.Equal(2000001, row.Signal.SourceNpcCode); Assert.Equal(700u, row.Signal.Actor);
        Assert.Equal(0, row.Signal.Confidence); Assert.Empty(engine.Snapshot(2200).Bars); Assert.Empty(engine.TakeAlerts());
        Assert.Empty(BossFightAnalyzer.Analyze(records));
        adapter.Process(new PlayerSeenEvent(2300, 800, "Player", 2305, GameClass.Gladiator));
        adapter.Process(new CastEvent(3000, 800, 0, 1800150));
        Assert.Single(records, r => r.Signal.Kind == BossSignalKind.UnmappedCast);
    }
    [Fact]
    public void Invalid_windup_or_warning_lifetime_is_rejected()
    {
        var profile = Catalog.Find(2320171, 1110, false)!; var rule = profile.Mechanics[0];
        Assert.NotNull(BossProfileCatalog.Validate(profile with { Mechanics = [rule with { AfterSkillId = null, AfterSeconds = null }] }));
        Assert.NotNull(BossProfileCatalog.Validate(profile with { Mechanics = [rule with { AlertSeconds = double.NaN }] }));
        Assert.NotNull(BossProfileCatalog.Validate(profile with { QuietSkills = null! }));
    }
    [Fact]
    public void Missed_Wave_does_not_leave_a_stuck_preparation_bar_or_repeat_itself()
    {
        var engine = new BossAssistantEngine(Catalog); engine.Process(S(1000, BossSignalKind.Pull));
        engine.Process(S(2000, BossSignalKind.Cast, 1800150));
        Assert.Empty(engine.Snapshot(11151).Bars); Assert.Empty(engine.Snapshot(14000).Bars);
    }
}
