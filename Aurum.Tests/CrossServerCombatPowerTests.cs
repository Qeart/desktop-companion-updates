using System.Text;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace Aurum.Tests;

public class CrossServerCombatPowerTests
{
    // Synthetic roster using the documented visitor layout. No user packet data.
    private static byte[] Body(ushort home = 2308, ushort world = 2313, byte tag = 4, ulong visitorCp = 86760)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(55u); w.Write((byte)0); w.Write((byte)5); w.Write(600091u); w.Write((ushort)0);
        w.Write((2305UL << 48) | 2); w.Write(new byte[3]); w.Write((byte)3);
        void Member(byte slot, string name, ushort server, ushort current, ulong cp, byte marker)
        {
            w.Write((byte)0x2C); w.Write(slot); w.Write(((ulong)server << 48) | slot);
            w.Write((byte)Encoding.UTF8.GetByteCount(name)); w.Write(Encoding.UTF8.GetBytes(name));
            w.Write(6u); w.Write(45u); w.Write(2800u);
            w.Write((byte)7); w.Write(current); w.Write((ushort)4301); w.Write(marker); w.Write(cp); w.Write(new byte[3]);
        }
        Member(1, "Visitor", home, world, visitorCp, tag);
        Member(2, "Self", 2305, 2305, 189392, 4);
        Member(3, "Teammate", 2306, 2306, 164210, 4);
        return stream.ToArray();
    }
    private static List<GameEvent> Parse(byte[] body)
    {
        var rows = new List<GameEvent>(); new PacketParser(GameData.Empty, rows.Add).Handle(Opcodes.PartyRoster, body); return rows;
    }
    [Theory]
    [InlineData(2308, 2302)] [InlineData(2308, 2313)] [InlineData(2311, 2313)]
    public void Visitor_CP_and_all_following_members_survive_current_world_different_from_home(int home, int current)
    {
        var roster = Assert.IsType<PartyRosterEvent>(Assert.Single(Parse(Body((ushort)home, (ushort)current))));
        Assert.True(roster.Complete); Assert.Equal(3, roster.Members.Count);
        Assert.Equal(new long[] { 86760, 189392, 164210 }, roster.Members.Select(m => m.CombatPower));
        Assert.Equal(home, roster.Members[0].ServerId); // do not replace character identity with current world
    }
    [Theory]
    [InlineData(0, 86760UL)] [InlineData(16, 86760UL)] [InlineData(4, 22210564UL)]
    public void Invalid_tag_or_shifted_CP_cannot_create_a_roster(int tag, ulong cp)
        => Assert.Empty(Parse(Body(tag: (byte)tag, visitorCp: cp)));

    [Fact]
    public void Parsed_CP_reaches_dungeon_rows_including_serverless_announcements_and_new_actor_IDs()
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { GroupOnly = true, TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        tracker.Process(new ZoneChangedEvent(100, 600091, "Dungeon", true));
        tracker.Process(new SelfIdentifiedEvent(200, 100, "Self", 2305, GameClass.Gladiator));
        foreach (var row in Parse(Body())) tracker.Process(row);
        tracker.Process(new PlayerSeenEvent(300, 101, "Visitor", 0, GameClass.Gladiator));
        tracker.Process(new PlayerSeenEvent(300, 102, "Teammate", 0, GameClass.Gladiator));
        tracker.Process(new DamageEvent(1000, 101, 500, 11020000, 1000, HitFlags.None));
        tracker.Process(new DamageEvent(1100, 102, 500, 11020000, 1000, HitFlags.None));
        var snapshot = tracker.Snapshot(null, 1200)!;
        Assert.Equal(86760, snapshot.Combatants.Single(p => p.ActorId == 101).CombatPower);
        Assert.Equal(164210, snapshot.Combatants.Single(p => p.ActorId == 102).CombatPower);
        tracker.Process(new ZoneChangedEvent(2000, 600092, "Next dungeon", true));
        tracker.Process(new PlayerSeenEvent(2100, 201, "Visitor", 0, GameClass.Gladiator));
        tracker.Process(new DamageEvent(2200, 201, 600, 11020000, 1000, HitFlags.None));
        Assert.Equal(86760, Assert.Single(tracker.Snapshot(null, 2300)!.Combatants).CombatPower);
    }
}
