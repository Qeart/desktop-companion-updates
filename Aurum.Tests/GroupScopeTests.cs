using System.Text;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace Aurum.Tests;

public class GroupScopeTests
{
    private static CombatTracker Tracker(bool self = true)
    {
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { TargetMode = TargetMode.All, GroupOnly = true, MinSavedFightMs = 0 });
        if (self) tracker.Process(new SelfIdentifiedEvent(100, 100, "Self", 2305, GameClass.Gladiator));
        return tracker;
    }
    private static PartyMemberInfo Member(string name, int server = 2305) => new(name, server, 45, 2800, 150000, GameClass.Gladiator);
    private static void Name(CombatTracker tracker, uint id, string name, int server = 2305) => tracker.Process(new PlayerSeenEvent(200, id, name, server, GameClass.Gladiator));
    private static void Hit(CombatTracker tracker, uint id, long amount = 1000, long at = 1000) => tracker.Process(new DamageEvent(at, id, 500, 11020000, amount, HitFlags.None));
    private static void Heal(CombatTracker tracker, uint id, long amount = 1000, long at = 2000) => tracker.Process(new HealEvent(at, id, 100, 17010000, amount, HitFlags.None));
    private static void Party(CombatTracker tracker, params string[] members) => tracker.Process(new PartyRosterEvent(500, new[] { Member("Self") }.Concat(members.Select(n => Member(n))).ToArray(), 55));
    [Fact]
    public void Nearby_strangers_cannot_start_a_fight_or_add_damage_and_healing()
    {
        var tracker = Tracker(); Name(tracker, 101, "Stranger"); Hit(tracker, 101, 900000); Assert.Null(tracker.Snapshot(null, 2000));
        Hit(tracker, 100); Hit(tracker, 101, 900000); Heal(tracker, 101, 900000); Heal(tracker, 100, 400);
        var snapshot = tracker.Snapshot(null, 3000)!;
        Assert.Equal(1000, snapshot.TotalDamage); Assert.Equal(400, snapshot.TotalHealing); Assert.Equal(100u, Assert.Single(snapshot.Combatants).ActorId);
    }
    [Fact]
    public void Only_own_roster_members_contribute_to_totals_percentages_and_history()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Name(tracker, 102, "Stranger"); Party(tracker, "Friend");
        Hit(tracker, 100, 1000); Hit(tracker, 101, 3000); Hit(tracker, 102, 900000); Heal(tracker, 101, 2000); Heal(tracker, 102, 900000);
        var snapshot = tracker.Snapshot(null, 3000)!;
        Assert.Equal(4000, snapshot.TotalDamage); Assert.Equal(2000, snapshot.TotalHealing); Assert.Equal(2, snapshot.Combatants.Count);
        Assert.Equal(.75, snapshot.Combatants.Single(p => p.ActorId == 101).Share);
        var record = tracker.Record(null)!; Assert.DoesNotContain(record.Summary.Combatants, p => p.ActorId == 102);
        Assert.Null(tracker.Detail(null, 102));
    }
    [Fact]
    public void A_namesake_on_another_server_is_not_a_member()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend", 2308); Party(tracker, "Friend"); Hit(tracker, 101); Assert.Null(tracker.Snapshot(null, 2000));
    }
    [Fact]
    public void A_name_without_server_needs_a_confirmed_identity_even_after_auxiliary_packets()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend", 0); Party(tracker, "Friend"); Hit(tracker, 101); Assert.Null(tracker.Snapshot(null, 1100));
        tracker.Process(new GroupMemberSeenEvent(1200, 101)); Hit(tracker, 101, at: 1500); Assert.Null(tracker.Snapshot(null, 1600));
        Name(tracker, 101, "Friend", 2305); Hit(tracker, 101, at: 1700); Assert.NotNull(tracker.Snapshot(null, 2000));
    }
    [Fact]
    public void A_scoped_spell_entity_is_not_ranked_as_a_player_without_an_owner()
    {
        var tracker = Tracker(); tracker.Process(new SummonSeenEvent(500, 701, 0, null, 2920011)); tracker.Process(new GroupMemberSeenEvent(700, 701));
        Hit(tracker, 701); Assert.Null(tracker.Snapshot(null, 2000));
    }
    [Fact]
    public void Party_finder_rosters_do_not_authorize_other_parties()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Name(tracker, 102, "Stranger"); Party(tracker, "Friend");
        tracker.Process(new PartyRosterEvent(600, [Member("Stranger"), Member("Other")], 99));
        Hit(tracker, 102); Assert.Null(tracker.Snapshot(null, 2000)); Hit(tracker, 101); Assert.Equal(101u, Assert.Single(tracker.Snapshot(null, 2000)!.Combatants).ActorId);
    }
    [Fact]
    public void A_roster_received_before_identity_is_used_once_self_is_known()
    {
        var tracker = Tracker(false); Name(tracker, 101, "Friend"); Party(tracker, "Friend");
        tracker.Process(new SelfIdentifiedEvent(700, 100, "Self", 2305, GameClass.Gladiator)); Hit(tracker, 101);
        Assert.Equal(101u, Assert.Single(tracker.Snapshot(null, 2000)!.Combatants).ActorId);
    }
    [Fact]
    public void Cached_names_and_combat_power_do_not_prove_membership()
    {
        var tracker = Tracker(false);
        tracker.ImportCache(new SessionState(100, 1110, [new(100, "Self", GameClass.Gladiator, 2305), new(101, "OldFriend", GameClass.Gladiator, 2305, 2800, 150000)], []));
        Hit(tracker, 101); Assert.Null(tracker.Snapshot(null, 2000)); Hit(tracker, 100); Assert.NotNull(tracker.Snapshot(null, 2000));
    }
    [Fact]
    public void Hp_and_scope_messages_alone_cannot_authorize_members_in_solo()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend");
        tracker.Process(new NpcHpEvent(500, 101, 100, 100, IsNpc: false)); tracker.Process(new GroupMemberSeenEvent(700, 101));
        Hit(tracker, 101); Heal(tracker, 101, 500); Assert.Null(tracker.Snapshot(null, 2000));
        Hit(tracker, 100, 1, at: 2500); Assert.Equal(1, tracker.Snapshot(null, 3000)!.TotalDamage);
    }
    [Fact]
    public void Leaving_the_group_clears_current_rows_and_drops_old_member_leases()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Party(tracker, "Friend"); Hit(tracker, 101);
        tracker.Process(new GroupLeftEvent(2000)); Assert.Null(tracker.Snapshot(null, 2500));
        Hit(tracker, 101, 9999, at: 3000); Assert.Null(tracker.Snapshot(null, 3500)); Hit(tracker, 100, 1, at: 4000);
        Assert.Equal(100u, Assert.Single(tracker.Snapshot(null, 4500)!.Combatants).ActorId);
    }
    [Fact]
    public void Resending_the_same_roster_does_not_reset_a_fight()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Party(tracker, "Friend"); Hit(tracker, 101); var before = tracker.Snapshot(null, 1500)!.Id;
        Party(tracker, "Friend"); Assert.Equal(before, tracker.Snapshot(null, 2000)!.Id);
    }
    [Fact]
    public void A_complete_membership_change_starts_a_new_segment()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Party(tracker, "Friend"); Hit(tracker, 101);
        tracker.Process(new PartyRosterEvent(2000, [Member("Self")], 55)); Assert.Null(tracker.Snapshot(null, 2500));
        Hit(tracker, 101, at: 3000); Assert.Null(tracker.Snapshot(null, 3500)); Hit(tracker, 100, at: 4000);
        Assert.Equal(100u, Assert.Single(tracker.Snapshot(null, 4500)!.Combatants).ActorId);
    }
    [Fact]
    public void A_partial_roster_cannot_remove_confirmed_members()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Party(tracker, "Friend");
        tracker.Process(new PartyRosterEvent(600, [Member("Self")], 55, Complete: false)); Hit(tracker, 101); Assert.NotNull(tracker.Snapshot(null, 2000));
    }
    [Fact]
    public void Group_pets_are_counted_for_their_owner_and_outside_pets_are_excluded()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Name(tracker, 102, "Stranger"); Party(tracker, "Friend");
        tracker.Process(new SummonSeenEvent(700, 701, 101, null, 2920011)); tracker.Process(new SummonSeenEvent(700, 702, 102, null, 2920011));
        Hit(tracker, 701, 500); Hit(tracker, 702, 90000);
        var snapshot = tracker.Snapshot(null, 2000)!; Assert.Equal(500, snapshot.TotalDamage); Assert.Equal(101u, Assert.Single(snapshot.Combatants).ActorId);
    }
    [Fact]
    public void A_late_pet_owner_link_recovers_only_confirmed_group_damage()
    {
        var tracker = Tracker(); Name(tracker, 101, "Friend"); Party(tracker, "Friend");
        tracker.Process(new SummonSeenEvent(700, 701, 0, null, 2920011)); Hit(tracker, 701, 500); Assert.Null(tracker.Snapshot(null, 1500));
        tracker.Process(new SummonSeenEvent(1700, 701, 101, null, 2920011));
        Assert.Equal(500, tracker.Snapshot(null, 2000)!.TotalDamage);
    }
    [Fact]
    public void All_twelve_members_of_a_force_roster_are_eligible()
    {
        var tracker = Tracker(); var names = Enumerable.Range(1, 11).Select(i => "Member" + i).ToArray(); Party(tracker, names);
        Hit(tracker, 100);
        for (var i = 0; i < names.Length; i++) { Name(tracker, (uint)(101 + i), names[i]); Hit(tracker, (uint)(101 + i)); }
        var snapshot = tracker.Snapshot(null, 2000)!; Assert.Equal(12, snapshot.Combatants.Count); Assert.Equal(12000, snapshot.TotalDamage);
    }
    [Theory]
    [InlineData(0x1392)] [InlineData(0x1D97)]
    public void Only_the_exact_party_leave_record_clears_membership(int opcode)
    {
        var events = new List<GameEvent>(); var parser = new PacketParser(GameData.Empty, events.Add);
        parser.Handle((ushort)opcode, [0]); parser.Handle((ushort)opcode, [1, 0]); parser.Handle((ushort)opcode, [0, 0, 0]); Assert.Empty(events);
        parser.Handle((ushort)opcode, [0, 0]); Assert.IsType<GroupLeftEvent>(Assert.Single(events));
    }
    private static byte[] RosterWithVacancies()
    {
        var bytes = new List<byte>(); bytes.AddRange(BitConverter.GetBytes(55u)); bytes.Add(5); bytes.AddRange("Force"u8.ToArray()); bytes.Add(12);
        bytes.AddRange(new byte[17]); bytes.Add(6);
        for (var slot = 1; slot <= 6; slot++)
        {
            if (slot is not (1 or 5)) { bytes.Add(0); bytes.Add((byte)slot); bytes.AddRange(new byte[33]); continue; }
            var name = slot == 1 ? "Self" : "Friend"; bytes.Add(0x2C); bytes.Add((byte)slot); bytes.AddRange(BitConverter.GetBytes((2305UL << 48) | (uint)slot));
            bytes.Add((byte)name.Length); bytes.AddRange(Encoding.UTF8.GetBytes(name)); bytes.AddRange(BitConverter.GetBytes(6u)); bytes.AddRange(BitConverter.GetBytes(45u)); bytes.AddRange(BitConverter.GetBytes(2800u));
            bytes.Add(1); bytes.AddRange(BitConverter.GetBytes((ushort)2305)); bytes.AddRange(BitConverter.GetBytes((ushort)2305)); bytes.Add(0); bytes.AddRange(BitConverter.GetBytes(150000UL)); bytes.AddRange(new byte[3]);
        }
        return bytes.ToArray();
    }
    [Fact]
    public void Roster_reader_keeps_later_members_after_vacant_slots_and_marks_completeness()
    {
        var events = new List<GameEvent>(); var parser = new PacketParser(GameData.Empty, events.Add); parser.Handle(Opcodes.PartyRoster, RosterWithVacancies());
        var roster = Assert.IsType<PartyRosterEvent>(Assert.Single(events)); Assert.True(roster.Complete); Assert.Equal(55u, roster.GroupId);
        Assert.Equal(new[] { "Self", "Friend" }, roster.Members.Select(m => m.Name));
    }
}
