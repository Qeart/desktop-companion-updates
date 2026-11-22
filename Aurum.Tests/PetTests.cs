using System.Buffers.Binary;
using AionMeter.Core.Events;
using AionMeter.Core.Pets;

namespace Aurum.Tests;
public class PetTests
{
    private static PetProgress Progress(int level = 2, int points = 40, double chance = 1)
    {
        var p = new PetProgress([new(1006, "Sparkie", [new(2101, "Sparkie mob", chance)])]);
        p.Process(new SelfIdentifiedEvent(0, 100, "Hero", 2314, GameClass.Chanter));
        p.Process(new PetCollectionEvent(0, [new(1006, level)], [new(1006, points)]));
        return p;
    }
    [Theory]
    [InlineData(0, 0, 105)] [InlineData(0, 4, 101)] [InlineData(1, 0, 100)]
    [InlineData(1, 24, 76)] [InlineData(2, 40, 35)] [InlineData(3, 0, 0)]
    public void Remaining_includes_all_levels(int level, int points, int remaining)
        => Assert.Equal(remaining, new PetState(level, points).Remaining);
    [Fact]
    public void Soul_gain_carries_excess_and_level_message_does_not_double_subtract()
    {
        var p = Progress(1, 24); p.Process(new PetSoulsEvent(1000, [new(1006, 5)]));
        Assert.Equal(new PetState(2, 4), p.State(1006));
        p.Process(new PetLevelEvent(1100, 1006, 2, false));
        Assert.Equal(new PetState(2, 4), p.State(1006));
    }
    [Fact]
    public void Own_kill_produces_updated_mob_count_after_loot()
    {
        var p = Progress(); p.Process(new NpcSeenEvent(100, 500, 2101));
        p.Process(new DamageEvent(900, 100, 500, 18010000, 1, HitFlags.None));
        p.Process(new DeathEvent(1000, 500)); p.Process(new PetSoulsEvent(1100, [new(1006, 1)]));
        Assert.Empty(p.TakeNotices(1200)); var n = Assert.Single(p.TakeNotices(1400));
        Assert.Equal(34, n.Remaining); Assert.Equal(34, n.Mobs); Assert.False(n.Estimated); Assert.Equal(1, n.Gained);
        Assert.Equal(1006, n.PetId);
        Assert.Empty(p.TakeNotices(1500));
    }
    [Fact]
    public void Random_drop_is_an_estimate_and_others_kills_are_ignored()
    {
        var p = Progress(chance: .5); p.Process(new NpcSeenEvent(100, 500, 2101));
        p.Process(new DamageEvent(900, 999, 500, 18010000, 1, HitFlags.None)); p.Process(new DeathEvent(1000, 500));
        Assert.Empty(p.TakeNotices(2000));
        p.Process(new DamageEvent(2900, 100, 500, 18010000, 1, HitFlags.None)); p.Process(new DeathEvent(3000, 500));
        Assert.Empty(p.TakeNotices(4000));
        p.Process(new PetSoulsEvent(4100, [new(1006, 2)]));
        var n = Assert.Single(p.TakeNotices(4400)); Assert.Equal(66, n.Mobs); Assert.True(n.Estimated); Assert.Equal(2, n.Gained);
    }
    [Fact]
    public void Unknown_progress_is_not_guessed()
    {
        var p = new PetProgress([new(1006, "Sparkie", [])]); p.Process(new PetSoulsEvent(1000, [new(1006, 1)]));
        var n = Assert.Single(p.TakeNotices(1500)); Assert.False(n.Synced); Assert.Null(n.Remaining); Assert.Null(n.Mobs);
    }
    [Fact]
    public void Collection_before_identity_is_bound_to_correct_profile()
    {
        var p = new PetProgress([]); p.Process(new PetCollectionEvent(1000, [new(1006, 2)], [new(1006, 70)]));
        Assert.False(p.Synced); p.Process(new SelfIdentifiedEvent(1100, 100, "Hero", 2314, GameClass.Chanter));
        Assert.Equal(5, p.State(1006)!.Remaining);
        p.Process(new SelfIdentifiedEvent(1200, 200, "Other", 2315, GameClass.Chanter)); Assert.False(p.Synced);
    }
    [Fact]
    public void Multiple_soul_gains_can_reach_max_in_one_packet()
    {
        var p = Progress(2, 74); p.Process(new PetSoulsEvent(1000, [new(1006, 1), new(1007, 5)]));
        var notices = p.TakeNotices(1300); Assert.Equal(2, notices.Count);
        Assert.Equal(new[] { 1006, 1007 }, notices.Select(n => n.PetId).Order().ToArray());
        Assert.Equal(0, notices.Single(n => n.PetName == "Sparkie").Remaining); Assert.Equal(new PetState(1, 0), p.State(1007));
    }
    [Fact]
    public void Soul_pickup_for_max_pet_still_notifies_full()
    {
        var p = Progress(3, 0);
        p.Process(new PetSoulsEvent(1000, [new(1006, 1)]));
        var notice = Assert.Single(p.TakeNotices(1300));
        Assert.True(notice.Synced); Assert.Equal(3, notice.Level);
        Assert.Equal(0, notice.Remaining); Assert.Equal(new PetState(3, 0), p.State(1006));
    }
    [Fact]
    public void All_pickups_in_a_batch_are_reported_without_three_pet_limit()
    {
        var p = Progress();
        p.Process(new PetSoulsEvent(1000, Enumerable.Range(1006, 5).Select(id => new PetValue(id, 1)).ToArray()));
        Assert.Equal(5, p.TakeNotices(1300).Count);
    }
    [Fact]
    public void Soul_packet_reads_all_entries_and_rejects_truncation()
    {
        var b = new byte[17]; b[0] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), 1006); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(9), 1007); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(13), 2);
        var result = Assert.IsType<PetSoulsEvent>(PetPacketReader.Read(0x0D90, b, 1)); Assert.Equal(2, result.Gains.Count);
        for (var i = 0; i < b.Length; i++) Assert.Null(PetPacketReader.Read(0x0D90, b.AsSpan(0, i), 1));
    }
    [Fact]
    public void Collection_packet_validates_levels_and_points()
    {
        var b = new byte[26]; b[4] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), 1006); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(13), 2);
        b[17] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), 1006); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), 40);
        var result = Assert.IsType<PetCollectionEvent>(PetPacketReader.Read(0x0090, b, 0));
        Assert.Equal(2, Assert.Single(result.Levels).Value); Assert.Equal(40, Assert.Single(result.Points).Value);
        b[13] = 9; Assert.Null(PetPacketReader.Read(0x0090, b, 0));
    }
}
