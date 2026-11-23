using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Gear;
using AionMeter.Core.Protocol;
using AionMeter.Core.Storage;

namespace Aurum.Tests;

public class LocalPlayerResolverTests
{
    // Live Global unchanged-stat frame from 23 November: actor 13776, zero
    // changed stats, eight-byte tail. Zero-count frames are not empty evidence.
    [Fact]
    public void Live_unchanged_stats_record_identifies_the_actual_actor()
    {
        var events = new List<GameEvent>(); var parser = new PacketParser(GameData.Empty, events.Add) { TimeMs = 1000 };
        parser.Handle(Opcodes.OwnStats, Convert.FromHexString("D06B000000000000000000"));
        var stats = Assert.IsType<OwnStatsEvent>(Assert.Single(events)); Assert.Equal(13776u, stats.ActorId);
    }
    [Theory]
    [InlineData("")][InlineData("00000000000000000000")]
    [InlineData("D06B010000000000000000")][InlineData("D06B0000000000000000")]
    [InlineData("D06B00000000000000000000")][InlineData("8080808080")]
    public void Invalid_truncated_or_extra_stat_bytes_cannot_establish_ownership(string hex)
        => Assert.Null(OwnStatsReader.Actor(Convert.FromHexString(hex)));

    [Fact]
    public void Populated_stat_frame_must_match_its_declared_count_exactly()
    {
        byte[] body = [0x64, 1, 0x41, 0, 0x6c, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(100u, OwnStatsReader.Actor(body));
        body[1] = 2; Assert.Null(OwnStatsReader.Actor(body));
    }
    private static void Stats(LocalPlayerResolver resolver, uint actor, long at = 1000)
    {
        resolver.Process(new OwnStatsEvent(at, actor)); resolver.Process(new OwnStatsEvent(at + 100, actor)); resolver.Process(new OwnStatsEvent(at + 200, actor));
    }
    private static DamageEvent Hit(long at, uint actor = 100, int skill = 18010000, long damage = 100)
        => new(at, actor, 900, skill, damage, HitFlags.None);

    [Fact]
    public void Late_start_without_self_record_recovers_identity_class_and_first_hits_once()
    {
        var resolver = new LocalPlayerResolver(); var tracker = new CombatTracker(GameData.Empty, new MeterOptions { GroupOnly = true });
        resolver.EventDecoded += tracker.Process;
        resolver.Process(new PlayerSeenEvent(10, 200, "Nearby", 2314, GameClass.Sorcerer));
        resolver.Process(new PlayerSeenEvent(20, 100, "Local", 2314, GameClass.Unknown));
        resolver.Process(new NpcSeenEvent(30, 900, 0, 10000, "Target"));
        resolver.Process(Hit(100)); resolver.Process(Hit(200)); resolver.Process(Hit(250, 200, 16010000, 9999));
        Assert.Null(tracker.Snapshot(null, 300)); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        Stats(resolver, 100);
        Assert.Equal("Ready", resolver.State.Stage); Assert.Equal("OwnStats", resolver.State.Source);
        Assert.Equal("Local", tracker.SelfName); Assert.Equal(GameClass.Chanter, tracker.SelfClass);
        Assert.Equal(200, tracker.Snapshot(null, 1300)!.TotalDamage);
        Stats(resolver, 100, 1400); Assert.Equal(200, tracker.Snapshot(null, 1700)!.TotalDamage);
        Assert.Equal(0, resolver.State.BufferedEvents);
    }

    [Fact]
    public void Stats_alone_bind_the_actor_but_do_not_guess_a_name_or_class_from_the_last_character()
    {
        var resolver = new LocalPlayerResolver([new("Previous", 2314, GameClass.Sorcerer)]);
        var tracker = new CombatTracker(GameData.Empty, new()); resolver.EventDecoded += tracker.Process;
        Stats(resolver, 100);
        Assert.Equal(100u, resolver.State.ActorId); Assert.Equal("", resolver.State.Name); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        resolver.Process(new CastEvent(1300, 100, 900, 18010000));
        Assert.Equal(GameClass.Chanter, tracker.SelfClass); Assert.Equal("", tracker.SelfName);
        resolver.Process(new PlayerSeenEvent(1400, 100, "Current", 2314, GameClass.Unknown));
        Assert.Equal("Current", tracker.SelfName);
    }

    [Fact]
    public void Class_is_learned_only_from_the_confirmed_actors_actions()
    {
        var resolver = new LocalPlayerResolver(); Stats(resolver, 100);
        resolver.Process(new CastEvent(1300, 200, 900, 16010000));
        Assert.Equal(GameClass.Unknown, resolver.State.Class);
        resolver.Process(Hit(1400)); Assert.Equal(GameClass.Unknown, resolver.State.Class);
        resolver.Process(Hit(1500)); Assert.Equal(GameClass.Chanter, resolver.State.Class);
    }

    [Fact]
    public void Stable_class_is_reused_only_after_a_live_actor_name_and_server_match()
    {
        var resolver = new LocalPlayerResolver([new("Local", 2314, GameClass.Chanter)]);
        resolver.Process(new PlayerSeenEvent(10, 100, "Local", 2314, GameClass.Unknown));
        Assert.Null(resolver.State.ActorId); Assert.Equal(GameClass.Unknown, resolver.State.Class);
        Stats(resolver, 100); Assert.Equal(GameClass.Chanter, resolver.State.Class);
        var otherServer = new LocalPlayerResolver(resolver.KnownCharacters);
        otherServer.Process(new PlayerSeenEvent(10, 100, "Local", 2315, GameClass.Unknown));
        Stats(otherServer, 100); Assert.Equal(GameClass.Unknown, otherServer.State.Class);
    }

    [Fact]
    public void Nearby_identity_health_loot_and_scope_events_never_choose_the_local_player()
    {
        var resolver = new LocalPlayerResolver([new("Local", 2314, GameClass.Chanter)]);
        resolver.Process(new PlayerSeenEvent(10, 100, "Local", 2314, GameClass.Chanter));
        for (var i = 0; i < 30; i++) {
            resolver.Process(new NpcHpEvent(i + 20, 100, 1000, 2000, false));
            resolver.Process(new GroupMemberSeenEvent(i + 20, 100));
            resolver.Process(Hit(i + 20));
        }
        Assert.Null(resolver.State.ActorId);
        resolver.Process(new OwnStatsEvent(1000, 100)); resolver.Process(new OwnStatsEvent(1100, 100));
        Assert.Null(resolver.State.ActorId);
        resolver.Process(new OwnStatsEvent(1200, 200)); resolver.Process(new OwnStatsEvent(1300, 100));
        Assert.Null(resolver.State.ActorId);
    }

    [Fact]
    public void Npcs_and_unowned_effects_cannot_be_elected_by_stat_evidence()
    {
        var resolver = new LocalPlayerResolver(); resolver.Process(new NpcSeenEvent(1, 900, 123));
        Stats(resolver, 900); Assert.Null(resolver.State.ActorId);
        resolver.Process(new SummonSeenEvent(1500, 800, 0, null, 123));
        Stats(resolver, 800, 2000); Assert.Null(resolver.State.ActorId);
    }

    [Fact]
    public void A_new_zone_or_changed_stat_actor_cannot_inherit_old_names_classes_or_rosters()
    {
        var resolver = new LocalPlayerResolver(); var tracker = new CombatTracker(GameData.Empty, new());
        resolver.EventDecoded += tracker.Process;
        resolver.Process(new SelfIdentifiedEvent(1, 100, "First", 2314, GameClass.Chanter));
        resolver.Process(new ZoneChangedEvent(10, 20, "World", false));
        Assert.Null(resolver.State.ActorId); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        Stats(resolver, 200); Assert.Equal("", tracker.SelfName); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        resolver.Process(new SelfIdentifiedEvent(1300, 200, "Second", 2315, GameClass.Assassin));
        Assert.Equal(GameClass.Assassin, tracker.SelfClass);
        Stats(resolver, 300, 2000); Assert.Equal("", tracker.SelfName); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
    }

    [Fact]
    public void Old_stat_frames_do_not_override_a_newer_official_self_record()
    {
        var resolver = new LocalPlayerResolver(); Stats(resolver, 100);
        resolver.Process(new SelfIdentifiedEvent(5000, 200, "Current", 2314, GameClass.Cleric));
        Stats(resolver, 100, 2000); Assert.Equal(200u, resolver.State.ActorId); Assert.Equal("SelfRecord", resolver.State.Source);
    }

    [Fact]
    public void Reused_cached_actor_does_not_supply_a_wrong_class_to_the_live_stats_binding()
    {
        var tracker = new CombatTracker(GameData.Empty, new());
        tracker.ImportCache(new(null, 0, [new(100, "Previous", GameClass.Sorcerer, 2314)], []));
        var resolver = new LocalPlayerResolver(); resolver.EventDecoded += tracker.Process;
        Stats(resolver, 100); Assert.Equal("", tracker.SelfName); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        resolver.Process(new CastEvent(1500, 100, 900, 18010000)); Assert.Equal(GameClass.Chanter, tracker.SelfClass);
    }

    [Fact]
    public void Waiting_buffer_is_bounded_and_old_combat_is_not_counted_after_delayed_identity()
    {
        var resolver = new LocalPlayerResolver(); var events = new List<GameEvent>(); resolver.EventDecoded += events.Add;
        for (var i = 0; i < 1100; i++) resolver.Process(Hit(i));
        Assert.Equal(1024, resolver.State.BufferedEvents); Assert.Empty(events);
        Stats(resolver, 100, 20000); Assert.DoesNotContain(events, e => e is DamageEvent);
    }

    [Fact]
    public void An_expired_session_can_migrate_character_metadata_without_restoring_the_actor_id()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Aurum-identity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "names-cache.json"); var old = DateTimeOffset.UtcNow.AddDays(-2).ToString("O");
            File.WriteAllText(path, $$"""{"SavedAt":"{{old}}","SelfId":100,"MapId":50,"Players":[{"Id":100,"Name":"Local","Class":"Chanter","ServerId":2314}],"Npcs":[]}""");
            var names = new NameCache(path); Assert.Null(names.Load()); Assert.Equal(new("Local", 2314, GameClass.Chanter), names.LoadCharacter());
            var storePath = Path.Combine(dir, "known-characters.json"); var store = new KnownCharacterStore(storePath);
            store.Save([names.LoadCharacter()!]); Assert.Equal(GameClass.Chanter, Assert.Single(store.Load()).Class);
            var json = File.ReadAllText(storePath); Assert.DoesNotContain("ActorId", json); Assert.DoesNotContain("SelfId", json); Assert.DoesNotContain("MapId", json);
            var resolver = new LocalPlayerResolver(store.Load()); Assert.Null(resolver.State.ActorId);
            File.WriteAllText(storePath, "{invalid json"); Assert.Empty(store.Load());
        } finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(GameClass.Gladiator, 11010000)] [InlineData(GameClass.Templar, 12010000)]
    [InlineData(GameClass.Assassin, 13010000)] [InlineData(GameClass.Ranger, 14010000)]
    [InlineData(GameClass.Sorcerer, 15010000)] [InlineData(GameClass.Elementalist, 16010000)]
    [InlineData(GameClass.Cleric, 17010000)] [InlineData(GameClass.Chanter, 18010000)]
    public void A_cold_start_enables_the_matching_item_and_arcana_profiles_for_every_class(GameClass expected, int skill)
    {
        var resolver = new LocalPlayerResolver(); var tracker = new CombatTracker(GameData.Empty, new());
        resolver.EventDecoded += tracker.Process;
        Stats(resolver, 100); resolver.Process(new CastEvent(1500, 100, 900, skill));
        Assert.Equal(expected, tracker.SelfClass);
        var data = Path.Combine(AppContext.BaseDirectory, "bis-data");
        var gear = GearAdviceCatalog.Load(Path.Combine(data, "gear-advice.json"));
        var arcana = ArcanaAdviceCatalog.Load(Path.Combine(data, "arcana-advice.json"));
        var itemAdvice = gear.For(tracker.SelfClass, new("Helm", GearSlot.Helmet, true, []));
        var cardAdvice = arcana.For(tracker.SelfClass, new("Mirror", ArcanaSlot.Mirror, "", null, [], 650));
        Assert.NotNull(itemAdvice); Assert.NotNull(cardAdvice); Assert.Equal(itemAdvice.ClassLabel, cardAdvice.ClassLabel);
    }

    [Fact]
    public void A_zone_transition_preserves_self_identity_in_the_outgoing_fight_record()
    {
        var resolver = new LocalPlayerResolver(); var tracker = new CombatTracker(GameData.Empty, new());
        resolver.EventDecoded += tracker.Process; FightRecord? saved = null; tracker.EncounterFinished += fight => saved = fight;
        resolver.Process(new SelfIdentifiedEvent(1, 100, "Local", 2314, GameClass.Chanter));
        resolver.Process(new NpcSeenEvent(2, 900, 0, 10000, "Target")); resolver.Process(Hit(100)); resolver.Process(Hit(1000));
        resolver.Process(new ZoneChangedEvent(2000, 50, "World", false));
        Assert.NotNull(saved); Assert.True(Assert.Single(saved.Summary.Combatants).IsSelf);
        Assert.Equal(200, saved.Summary.TotalDamage); Assert.Equal(GameClass.Unknown, tracker.SelfClass);
    }
}
