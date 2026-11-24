using System.Text.Json;
using AionMeter.Core.Bosses;

namespace Aurum.Tests;

public class BossPracticeTests
{
    private static readonly Guid Fight = Guid.NewGuid();
    private static BossLogRecord Row(long at, BossSignalKind kind, int skill = 0, int npc = 2300171, Guid? fight = null, uint actor = 7)
        => new(1, fight ?? Fight, new(at, kind, actor, npc) { BossName = "Ultimate Berk", MapId = 600001, SkillId = skill,
            SkillName = skill == 0 ? "" : "Fixture " + skill, Source = "Decoded-event fixture" });
    private static BossProfileCatalog Catalog => BossProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "boss-data", "bossmods"));
    [Fact]
    public void Krao_Cave_final_boss_is_Ultimate_Berk_and_all_twelve_forms_are_observable()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "boss-data", "npcs", "en.json")));
        foreach (var first in new[] { 2300171, 2310171, 2320171 }) foreach (var npc in Enumerable.Range(first, 4)) {
            var def = data.RootElement.GetProperty(npc.ToString());
            Assert.Equal("Ultimate Berk", def.GetProperty("name").GetString()); Assert.True(def.GetProperty("isBoss").GetBoolean());
            Assert.Contains(def.GetProperty("dungeonId").GetInt32(), new[] { 600001, 600002, 600004 });
            Assert.True(BossPracticeSession.IsBerk(npc));
        }
        Assert.False(BossPracticeSession.IsBerk(2300107));
        Assert.Equal(2, Catalog.Find(2300171, 600001, false)!.Mechanics.Count(m => m.AfterSeconds > 0 || m.CooldownSeconds > 0));
    }
    [Theory]
    [InlineData("off")] [InlineData("other")] [InlineData("demo")] [InlineData("early")] [InlineData("schema")]
    public void Recording_requires_explicit_start_and_only_accepts_real_Berk_records(string reason)
    {
        var test = new BossPracticeSession(); if (reason != "off") test.Start(1000);
        var row = Row(reason == "early" ? 999 : 2000, BossSignalKind.Cast, 1800053, reason == "other" ? 2300908 : 2300171);
        if (reason == "demo") row = row with { Signal = row.Signal with { Synthetic = true } };
        if (reason == "schema") row = row with { SchemaVersion = 2 };
        Assert.False(test.Observe(row)); Assert.Equal(0, test.Status.Events);
    }
    [Fact]
    public void Recording_a_different_form_does_not_assume_it_has_the_same_timers()
    {
        var test = new BossPracticeSession(); test.Start(1000);
        Assert.True(test.Observe(Row(2000, BossSignalKind.Cast, 1800053, 2300172)));
        var report = test.Report(Catalog, 3000); Assert.False(Assert.Single(report.Npcs).HasTimerProfile);
        Assert.Null(Catalog.Find(2300172, 600001, false)); Assert.Empty(report.TimingProposals);
    }
    [Fact]
    public void Human_marker_is_a_timestamp_with_no_automatically_assigned_skill()
    {
        var test = new BossPracticeSession(); test.Start(1000); Assert.Null(test.Mark(1100));
        test.Observe(Row(2000, BossSignalKind.Cast, 1800150)); var mark = test.Mark(2300)!;
        Assert.Equal(BossSignalKind.Mark, mark.Signal.Kind); Assert.Equal(0, mark.Signal.SkillId); Assert.Equal(Fight, mark.SessionId);
        Assert.Contains("unverified", mark.Signal.Source); Assert.Null(test.Mark(2400));
        test.Observe(Row(2500, BossSignalKind.Death)); Assert.Null(test.Mark(3000));
    }
    [Fact]
    public void Correlation_keeps_casts_and_hits_distinct_and_stays_in_the_same_encounter()
    {
        var test = new BossPracticeSession(); test.Start(1000);
        test.Observe(Row(4000, BossSignalKind.Cast, 1800150)); test.Observe(Row(4500, BossSignalKind.Hit, 1800053)); test.Mark(5000);
        test.Observe(Row(5500, BossSignalKind.Cast, 1800060));
        test.Observe(Row(5600, BossSignalKind.Cast, 999, fight: Guid.NewGuid(), actor: 8));
        test.Observe(Row(7100, BossSignalKind.Cast, 998));
        var report = test.Report(Catalog, 8000); var candidates = Assert.Single(report.Markers).Candidates;
        Assert.Equal(3, candidates.Count); Assert.DoesNotContain(candidates, x => x.SkillId is 999 or 998);
        Assert.Contains(candidates, x => x.Kind == BossSignalKind.Hit && x.SecondsFromMarker == -.5);
        Assert.Contains(candidates, x => x.Kind == BossSignalKind.Cast && x.SecondsFromMarker == .5);
        Assert.Empty(report.TimingProposals);
    }
    [Fact]
    public void Expired_or_stale_observations_cannot_get_a_new_marker()
    {
        var test = new BossPracticeSession(); test.Start(1000); test.Observe(Row(2000, BossSignalKind.Cast, 1800150));
        Assert.Null(test.Mark(12001)); Assert.True(test.Tick(901000)); Assert.False(test.IsRunning);
        Assert.False(test.Observe(Row(901001, BossSignalKind.Cast, 1800060))); Assert.Null(test.Mark(901002));
        Assert.Equal(901000, test.Status.StoppedAt);
    }
    [Fact]
    public void Buffer_is_bounded_and_reports_missing_records()
    {
        var test = new BossPracticeSession(8); test.Start(1000);
        for (var i = 0; i < 20; i++) test.Observe(Row(1000 + i * 1000, BossSignalKind.Hit, 1800053));
        Assert.Equal(8, test.Status.Events); Assert.Equal(12, test.Status.Dropped);
        var report = test.Report(Catalog, 30000); Assert.Equal(12, report.Dropped); Assert.Equal(8, report.Timeline.Count);
    }
    [Fact]
    public void Recorded_casts_from_two_fights_can_propose_a_cooldown_without_verifying_a_mechanic()
    {
        var test = new BossPracticeSession(); test.Start(1000);
        for (var pull = 0; pull < 2; pull++) { var id = Guid.NewGuid(); for (var i = 0; i < 5; i++) test.Observe(Row(2000 + pull * 100000 + i * 10000, BossSignalKind.Cast, 1800060, fight: id)); }
        test.Stop(160000); var report = test.Report(Catalog, 160000);
        var proposal = Assert.Single(report.TimingProposals); Assert.True(proposal.Eligible); Assert.Equal(10, proposal.MedianSeconds);
        Assert.Empty(report.Markers); Assert.Equal(2, test.Status.Fights);
    }
    [Fact]
    public void Exported_report_roundtrips_the_raw_timeline_and_an_explicit_stop()
    {
        var test = new BossPracticeSession(); test.Start(1000); test.Observe(Row(2000, BossSignalKind.Cast, 1800053)); test.Mark(3000); test.Stop(5000);
        Assert.False(test.Observe(Row(6000, BossSignalKind.Cast, 1800060)));
        var report = test.Report(Catalog, 8000); var saved = JsonSerializer.Deserialize<BossPracticeReport>(JsonSerializer.Serialize(report, BossJson.Options), BossJson.Options)!;
        Assert.Equal(5000, saved.StoppedAt); Assert.Equal(2, saved.Timeline.Count); Assert.Single(saved.Markers);
        Assert.Single(saved.Skills); Assert.True(saved.Npcs[0].HasTimerProfile);
    }
}
