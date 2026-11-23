using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum.Tests;

public class GroupZoneRecoveryTests
{
    private static PartyMemberInfo Member(string name) => new(name, 2305, 45, 2800, 150000, GameClass.Gladiator);
    private static (LocalPlayerResolver Resolver, CombatTracker Tracker) Group(int members = 2)
    {
        var resolver = new LocalPlayerResolver();
        var tracker = new CombatTracker(GameData.Empty, new MeterOptions { GroupOnly = true, TargetMode = TargetMode.All, MinSavedFightMs = 0 });
        resolver.EventDecoded += tracker.Process;
        resolver.Process(new SelfIdentifiedEvent(100, 100, "Self", 2305, GameClass.Gladiator));
        resolver.Process(new ZoneChangedEvent(200, 1110, "Start", false, true));
        resolver.Process(new PartyRosterEvent(300, new[] { Member("Self") }.Concat(Enumerable.Range(1, members - 1).Select(i => Member("Friend" + i))).ToArray(), 55));
        return (resolver, tracker);
    }
    private static void Name(LocalPlayerResolver resolver, uint actor = 201, string name = "Friend1", int server = 2305)
        => resolver.Process(new PlayerSeenEvent(1100, actor, name, server, GameClass.Gladiator));
    private static void Hit(LocalPlayerResolver resolver, uint actor = 201, long at = 1400, long amount = 1000)
        => resolver.Process(new DamageEvent(at, actor, 500, 11020000, amount, HitFlags.None));

    [Fact]
    public void Own_party_survives_the_real_resolver_zone_transition_with_new_actor_ids()
    {
        var (resolver, tracker) = Group();
        resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false));
        Name(resolver);
        resolver.Process(new SelfIdentifiedEvent(1200, 200, "Self", 2305, GameClass.Gladiator));
        Hit(resolver); resolver.Process(new HealEvent(1500, 201, 200, 17010000, 400, HitFlags.None));
        var fight = Assert.IsType<EncounterSnapshot>(tracker.Snapshot(null, 1600));
        Assert.Equal(1000, fight.TotalDamage); Assert.Equal(400, fight.TotalHealing);
        Assert.Equal(201u, Assert.Single(fight.Combatants).ActorId);
    }
    [Fact]
    public void Force_survives_zone_change_and_still_excludes_a_nearby_stranger()
    {
        var (resolver, tracker) = Group(12);
        resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false));
        resolver.Process(new SelfIdentifiedEvent(1200, 200, "Self", 2305, GameClass.Gladiator));
        for (var i = 1; i <= 11; i++) { Name(resolver, (uint)(200 + i), "Friend" + i); Hit(resolver, (uint)(200 + i)); }
        Name(resolver, 300, "Stranger"); Hit(resolver, 300, amount: 99999);
        var fight = Assert.IsType<EncounterSnapshot>(tracker.Snapshot(null, 1600));
        Assert.Equal(11000, fight.TotalDamage); Assert.Equal(11, fight.Combatants.Count);
        Assert.DoesNotContain(fight.Combatants, c => c.ActorId == 300);
    }
    [Fact]
    public void Anonymous_own_stat_recovery_keeps_party_suspended_until_same_character_is_confirmed()
    {
        var (resolver, tracker) = Group(); resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false)); Name(resolver);
        for (var i = 0; i < 3; i++) resolver.Process(new OwnStatsEvent(1200 + i, 200));
        Hit(resolver); Assert.Null(tracker.Snapshot(null, 1600));
        resolver.Process(new SelfIdentifiedEvent(1700, 200, "Self", 2305, GameClass.Gladiator));
        Hit(resolver, at: 1800); Assert.Equal(1000, tracker.Snapshot(null, 2000)!.TotalDamage);
    }
    [Theory]
    [InlineData("Other", 2305)] [InlineData("Self", 2308)]
    public void Another_character_cannot_inherit_the_suspended_party(string name, int server)
    {
        var (resolver, tracker) = Group(); resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false)); Name(resolver);
        resolver.Process(new SelfIdentifiedEvent(1200, 200, name, server, GameClass.Gladiator));
        Hit(resolver); Assert.Null(tracker.Snapshot(null, 1600));
        Hit(resolver, 200, at: 1700); Assert.Equal(200u, Assert.Single(tracker.Snapshot(null, 1800)!.Combatants).ActorId);
    }
    [Fact]
    public void Leaving_party_while_loading_cancels_the_suspended_membership()
    {
        var (resolver, tracker) = Group(); resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false));
        resolver.Process(new GroupLeftEvent(1050)); Name(resolver);
        resolver.Process(new SelfIdentifiedEvent(1200, 200, "Self", 2305, GameClass.Gladiator));
        Hit(resolver); Assert.Null(tracker.Snapshot(null, 1600));
    }
    [Fact]
    public void Roster_received_during_loading_is_replayed_after_identity_confirmation()
    {
        var (resolver, tracker) = Group(); resolver.Process(new ZoneChangedEvent(1000, 2010, "Open world", false));
        resolver.Process(new PartyRosterEvent(1050, [Member("Self"), Member("NewFriend")], 55));
        Name(resolver); Name(resolver, 202, "NewFriend");
        resolver.Process(new SelfIdentifiedEvent(1200, 200, "Self", 2305, GameClass.Gladiator));
        Hit(resolver); Hit(resolver, 202);
        Assert.Equal(202u, Assert.Single(tracker.Snapshot(null, 1600)!.Combatants).ActorId);
    }
}
