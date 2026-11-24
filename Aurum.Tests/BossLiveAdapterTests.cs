using System.Text.Json;
using AionMeter.Core.Bosses;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public class BossLiveAdapterTests
{
    private static (BossAssistantEngine Engine, BossLiveAdapter Adapter) Setup(bool member = true, BossSnapshot? context = null)
    {
        var data = GameData.Empty; data.Npcs[2300001] = new(2300001, "Fixture Boss", true, false); data.Skills[1800001] = "Fixture Skill";
        var profile = new BossProfile { Id = "fixture", Name = "Fixture Boss", NpcCodes = [2300001], Mechanics = [new() { Id = "skill", Name = "Fixture Skill", SkillId = 1800001, FirstSeconds = 20, CooldownSeconds = 30 }] };
        var engine = new BossAssistantEngine(new([profile]));
        return (engine, new(data, engine, (id, _) => member && id == 100, () => context, 1110));
    }
    [Fact]
    public void Nearby_boss_and_strangers_cannot_start_an_encounter()
    {
        var (engine, adapter) = Setup(); adapter.Process(new NpcSeenEvent(1000, 200, 2300001, 100000));
        adapter.Process(new CastEvent(1100, 200, 999, 1800001)); adapter.Process(new DamageEvent(1200, 999, 200, 1800001, 1, HitFlags.None));
        Assert.Empty(engine.Snapshot(1200).Fights);
        adapter.Process(new DamageEvent(1300, 100, 200, 1800001, 1, HitFlags.None));
        Assert.Single(engine.Snapshot(1300).Fights); Assert.Empty(engine.Snapshot(1300).Bars);
        adapter.Process(new CastEvent(1400, 200, 100, 1800001)); Assert.Equal(30, Assert.Single(engine.Snapshot(1400).Bars).Seconds);
    }
    [Fact]
    public void Hit_is_logged_as_a_hit_and_never_fabricates_cast_start_or_duration()
    {
        var (engine, adapter) = Setup(); var records = new List<BossLogRecord>(); engine.Record += records.Add;
        adapter.Process(new NpcSeenEvent(1000, 200, 2300001)); adapter.Process(new HitNoticeEvent(2000, 200, 100, 1800001));
        Assert.Single(engine.Snapshot(2000).Fights); Assert.Empty(engine.Snapshot(2000).Bars);
        Assert.Contains(records, r => r.Signal.Kind == BossSignalKind.Hit && r.Signal.SkillId == 1800001);
        Assert.DoesNotContain(records, r => r.Signal.Kind == BossSignalKind.Cast);
        Assert.Null(engine.Snapshot(2000).Fights[0].HpPercent);
    }
    [Fact]
    public void Confirmed_battle_toggle_can_anchor_the_first_timer_but_midfight_contact_cannot()
    {
        var (engine, adapter) = Setup(); adapter.Process(new NpcSeenEvent(1000, 200, 2300001));
        adapter.Process(new BattleStateEvent(2000, 200, true)); adapter.Process(new HitNoticeEvent(2100, 100, 200, 11020000));
        Assert.True(engine.Snapshot(2100).Fights[0].PullConfirmed); Assert.Equal(22000, Assert.Single(engine.Snapshot(2100).Bars).DueAt);
        adapter.Process(new BattleStateEvent(2200, 200, false)); Assert.Empty(engine.Snapshot(2200).Fights);
    }
    [Fact]
    public void Existing_dps_boss_context_recovers_a_missing_spawn_without_choosing_a_nearby_npc()
    {
        var (engine, adapter) = Setup(false, new BossSnapshot(200, 2300001, "Fixture Boss", 500, 1000));
        adapter.Process(new CastEvent(2000, 999, 100, 1800001)); Assert.Empty(engine.Snapshot(2000).Fights);
        adapter.Process(new CastEvent(2100, 200, 100, 1800001)); Assert.Single(engine.Snapshot(2100).Fights);
        Assert.Equal(50, engine.Snapshot(2100).Fights[0].HpPercent);
    }
    [Theory]
    [InlineData("zone")] [InlineData("identity")] [InlineData("death")] [InlineData("capture")]
    public void Transition_clears_live_timers(string transition)
    {
        var (engine, adapter) = Setup(); adapter.Process(new NpcSeenEvent(1000, 200, 2300001)); adapter.Process(new CastEvent(2000, 200, 100, 1800001));
        switch (transition) {
            case "zone": adapter.Process(new ZoneChangedEvent(3000, 2220, "Fixture zone", false)); break;
            case "identity": adapter.Process(new LocalIdentityLostEvent(3000)); break;
            case "death": adapter.Process(new DeathEvent(3000, 200)); break;
            default: adapter.Clear(3000, "Disconnected"); break;
        }
        Assert.Empty(engine.Snapshot(3000).Bars); Assert.Empty(engine.Snapshot(3000).Fights);
    }
    [Fact]
    public void Disabled_assistant_keeps_npc_metadata_but_does_not_create_fights()
    {
        var (engine, adapter) = Setup(); adapter.Enabled = false; adapter.Process(new NpcSeenEvent(1000, 200, 2300001)); adapter.Process(new CastEvent(2000, 200, 100, 1800001));
        Assert.Empty(engine.Snapshot(2000).Fights); adapter.Enabled = true; adapter.Process(new CastEvent(3000, 200, 100, 1800001)); Assert.Single(engine.Snapshot(3000).Bars);
    }
    [Fact]
    public void Real_journal_roundtrip_replays_the_same_deadline_and_excludes_demo_from_learning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aurum-boss-test-" + Guid.NewGuid().ToString("N"));
        try {
            var (engine, adapter) = Setup();
            using (var journal = new BossJournal(directory)) {
                engine.Record += journal.Add;
                adapter.Process(new NpcSeenEvent(1000, 200, 2300001)); adapter.Process(new CastEvent(2000, 200, 100, 1800001)); adapter.Process(new DeathEvent(40000, 200));
            }
            var rows = BossJournal.Read(Assert.Single(Directory.GetFiles(directory, "*.jsonl")));
            Assert.Equal(new[] { BossSignalKind.Pull, BossSignalKind.Cast, BossSignalKind.Death }, rows.Select(r => r.Signal.Kind));
            var (replayed, _) = Setup(); var replay = new BossReplay(replayed, rows.Select(r => r.Signal)); replay.AdvanceTo(5000);
            Assert.Equal(27, Assert.Single(replayed.Snapshot(5000).Bars).Seconds); replay.AdvanceTo(40000); Assert.Empty(replayed.Snapshot(40000).Bars);
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Shipped_profiles_are_valid_bosses_with_real_anchors_but_no_unverified_cast_durations()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "boss-data"); var catalog = BossProfileCatalog.Load(Path.Combine(root, "bossmods"));
        Assert.Empty(catalog.Errors); Assert.Equal(13, catalog.Profiles.Count(p => !p.Synthetic));
        var data = GameData.Load(root);
        foreach (var profile in catalog.Profiles.Where(p => !p.Synthetic)) {
            Assert.All(profile.NpcCodes, id => Assert.True(data.Npcs[id].IsBoss));
            Assert.All(profile.Mechanics, rule => { Assert.True(rule.CooldownSeconds > 0 || rule.AfterSeconds > 0 || rule.AlertOnCast && rule.Reaction.Length > 0); Assert.Null(rule.FirstSeconds); Assert.Null(rule.CastSeconds); Assert.True(rule.SkillId > 0); });
        }
        Assert.Single(catalog.Profiles, p => p.Synthetic);
    }
    [Fact]
    public void Malformed_profile_does_not_remove_other_valid_profiles_and_newer_local_version_wins()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aurum-profile-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{broken");
            var profile = new BossProfile { Id = "valid", Name = "Fixture", NpcCodes = [2300001] };
            File.WriteAllText(Path.Combine(directory, "one.json"), JsonSerializer.Serialize(profile, BossJson.Options));
            File.WriteAllText(Path.Combine(directory, "two.json"), JsonSerializer.Serialize(profile with { Version = 2, Name = "Corrected" }, BossJson.Options));
            var catalog = BossProfileCatalog.Load(directory); Assert.Single(catalog.Errors); Assert.Equal("Corrected", Assert.Single(catalog.Profiles).Name);
        } finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void Partial_or_invalid_replay_is_rejected_instead_of_inventing_events()
    {
        var file = Path.GetTempFileName();
        try { File.WriteAllText(file, "{\"schemaVersion\":1}"); Assert.Throws<InvalidDataException>(() => BossJournal.Read(file)); }
        finally { File.Delete(file); }
    }
    [Theory]
    [InlineData("player")] [InlineData("summon")] [InlineData("npc")]
    public void Reused_actor_id_never_leaves_a_boss_timer_on_a_player_or_summon(string replacement)
    {
        var (engine, adapter) = Setup(); adapter.Process(new NpcSeenEvent(1000, 200, 2300001)); adapter.Process(new CastEvent(2000, 200, 100, 1800001));
        GameEvent next = replacement switch {
            "player" => new PlayerSeenEvent(3000, 200, "Fixture player", 1, GameClass.Chanter),
            "summon" => new SummonSeenEvent(3000, 200, 100, null, 1),
            _ => new NpcSeenEvent(3000, 200, 123)
        };
        adapter.Process(next); Assert.Empty(engine.Snapshot(3000).Bars);
    }
    [Fact]
    public void A_late_hp_sample_cannot_replace_the_current_hp()
    {
        var (engine, adapter) = Setup(); adapter.Process(new NpcSeenEvent(1000, 200, 2300001));
        adapter.Process(new NpcHpEvent(3000, 200, 400, 1000)); adapter.Process(new NpcHpEvent(2000, 200, 900, 1000));
        adapter.Process(new CastEvent(4000, 200, 100, 1800001)); Assert.Equal(40, engine.Snapshot(4000).Fights[0].HpPercent);
    }
    [Theory]
    [InlineData("null")] [InlineData("large")]
    public void Empty_or_oversized_local_profile_is_reported_without_crashing_the_addon(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aurum-invalid-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            File.WriteAllText(Path.Combine(directory, "invalid.json"), content == "null" ? "null" : new string(' ', 512001));
            var catalog = BossProfileCatalog.Load(directory); Assert.Empty(catalog.Profiles); Assert.Single(catalog.Errors);
        } finally { Directory.Delete(directory, true); }
    }
}
