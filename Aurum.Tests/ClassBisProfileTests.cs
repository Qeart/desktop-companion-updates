using System.Text.Json;
using AionMeter.Core.Events;
using AionMeter.Core.Gear;

namespace Aurum.Tests;

public class ClassBisProfileTests
{
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "bis-data", name + "-advice.json");
    private static GearAdviceData Gear => JsonSerializer.Deserialize<GearAdviceData>(File.ReadAllText(Data("gear")))!;
    private static ArcanaAdviceData Arcana => JsonSerializer.Deserialize<ArcanaAdviceData>(File.ReadAllText(Data("arcana")))!;
    public static TheoryData<GameClass> Classes => new() { GameClass.Chanter, GameClass.Gladiator, GameClass.Templar,
        GameClass.Assassin, GameClass.Ranger, GameClass.Sorcerer, GameClass.Elementalist, GameClass.Cleric };

    [Fact]
    public void Shipped_profiles_cover_exactly_the_eight_global_classes_without_a_generic_build()
    {
        var expected = Enum.GetValues<GameClass>().Where(c => c is not GameClass.Unknown and not GameClass.Brawler).Select(c => c.ToString()).Order().ToArray();
        Assert.Equal(expected, Gear.Classes.Select(c => c.Class).Order());
        Assert.Equal(expected, Arcana.Classes.Select(c => c.Class).Order());
        Assert.Empty(Gear.Slots); Assert.Empty(Arcana.Cards);
        Assert.Equal(8, Gear.Classes.Select(c => c.Build).Distinct().Count());
        Assert.Equal(8, Arcana.Classes.Select(c => c.Build).Distinct().Count());
    }

    [Theory, MemberData(nameof(Classes))]
    public void Real_equipment_catalogue_has_a_complete_explicit_plan_for_every_slot(GameClass gameClass)
    {
        var data = Gear; var profile = data.Classes.Single(c => c.Class == gameClass.ToString());
        var slots = Enum.GetValues<GearSlot>().Where(s => s != GearSlot.Unknown).ToArray();
        Assert.Equal(slots.Length, profile.Slots!.Length);
        Assert.Equal(slots.Select(s => s.ToString()).Order(), profile.Slots.Select(s => s.Slot).Order());
        Assert.NotEmpty(profile.Priorities!); Assert.True(profile.Sources!.Length >= 2);
        Assert.All(profile.Sources, url => Assert.Equal("https", new Uri(url).Scheme));
        var catalog = GearAdviceCatalog.Load(Data("gear"));
        foreach (var slot in slots)
        {
            var item = new ItemTooltip("Fixture", slot, true, []);
            var advice = Assert.IsType<GearAdvice>(catalog.For(gameClass, item));
            Assert.Equal(profile.Build, advice.Build); Assert.Equal(profile.Label, advice.ClassLabel);
            Assert.NotEmpty(advice.Targets); Assert.False(string.IsNullOrWhiteSpace(advice.Note));
            Assert.Equal(advice.Targets.Count, advice.Targets.Select(t => t.Name).Distinct().Count());
            if (slot is GearSlot.Weapon or GearSlot.OffHand or GearSlot.Bracelet) Assert.Empty(advice.Skills);
            else if (slot == GearSlot.Ring) Assert.Equal(profile.Active, advice.Skills.Select(s => s.Name));
            else Assert.Equal(profile.Passive, advice.Skills.Select(s => s.Name));
        }
    }

    [Theory, MemberData(nameof(Classes))]
    public void Real_arcana_catalogue_only_recommends_legal_skills_and_matches_its_own_card_family(GameClass gameClass)
    {
        var profile = Arcana.Classes.Single(c => c.Class == gameClass.ToString());
        Assert.Equal(5, profile.Cards!.Length); Assert.Equal(5, profile.Goals.Count); Assert.Equal(5, profile.Pools!.Count);
        Assert.Equal(22, profile.Pools["Chalice"].Distinct().Count());
        Assert.Equal(6, profile.Pools["Parchment"].Length); Assert.Equal(6, profile.Pools["Compass"].Length);
        Assert.Equal(5, profile.Pools["Bell"].Length); Assert.Equal(5, profile.Pools["Mirror"].Length);
        Assert.Equal(profile.Pools["Chalice"].Order(), profile.Pools.Where(p => p.Key != "Chalice").SelectMany(p => p.Value).Order());
        var gear = Gear.Classes.Single(c => c.Class == profile.Class);
        var actives = profile.Pools["Parchment"].Concat(profile.Pools["Compass"]).ToArray();
        var passives = profile.Pools["Bell"].Concat(profile.Pools["Mirror"]).ToArray();
        Assert.All(gear.Active, name => Assert.Contains(name, actives));
        Assert.All(gear.Passive, name => Assert.Contains(name, passives));
        var catalog = ArcanaAdviceCatalog.Load(Data("arcana"));
        foreach (var card in profile.Cards)
        {
            Assert.True(Enum.TryParse<ArcanaSlot>(card.Slot, out var slot));
            var primary = profile.Goals[card.Slot]; var secondary = profile.SecondaryGoals![card.Slot];
            Assert.NotEmpty(primary); Assert.Empty(primary.Intersect(secondary));
            Assert.All(primary.Concat(secondary), name => Assert.Contains(name, profile.Pools[card.Slot]));
            var panel = new ArcanaPanel("Fixture", slot, card.Family, card.Stat,
                primary.Concat(secondary).Select(name => new ArcanaSkill(name, 2)).ToArray(), 650);
            var advice = Assert.IsType<ArcanaAdvice>(catalog.For(gameClass, panel));
            Assert.True(advice.MainMatches); Assert.Equal(profile.Plan, advice.Plan); Assert.Equal(profile.Build, advice.Build);
            Assert.All(advice.Skills.Concat(advice.SecondarySkills!), s => { Assert.True(s.Present); Assert.Equal(2, s.Bonus); });
            Assert.False(catalog.For(gameClass, panel with { Family = card.Family == "Magic" ? "Vigor" : "Magic" })!.MainMatches);
            // An ellipsised decorative title does not invalidate an unambiguous main stat.
            Assert.True(catalog.For(gameClass, panel with { Family = "", Title = "..." })!.MainMatches);
        }
    }

    [Theory]
    [InlineData(GameClass.Chanter, "Magic", "Wisdom [Lumiel]", 3)]
    [InlineData(GameClass.Gladiator, "Magic", "Wisdom [Lumiel]", 3)]
    [InlineData(GameClass.Templar, "Magic", "Wisdom [Lumiel]", 3)]
    [InlineData(GameClass.Assassin, "Vigor", "Illusion [Kaisinel]", 4)]
    [InlineData(GameClass.Ranger, "Magic", "Wisdom [Lumiel]", 2)]
    [InlineData(GameClass.Sorcerer, "Vigor", "Illusion [Kaisinel]", 4)]
    [InlineData(GameClass.Elementalist, "Magic", "Wisdom [Lumiel]", 3)]
    [InlineData(GameClass.Cleric, "Vigor", "Illusion [Kaisinel]", 4)]
    public void Selected_builds_do_not_inherit_the_old_four_vigor_plan(GameClass gameClass, string family, string stat, int vigor)
    {
        var profile = Arcana.Classes.Single(c => c.Class == gameClass.ToString());
        Assert.Equal(vigor, profile.Cards!.Count(c => c.Family == "Vigor"));
        Assert.Contains(vigor + "× Vigor", profile.Plan);
        var mirror = profile.Cards!.Single(c => c.Slot == "Mirror"); Assert.Equal(family, mirror.Family); Assert.Equal(stat, mirror.Stat);
        Assert.Contains("70%", profile.Plan);
    }

    [Fact]
    public void Switching_characters_changes_stats_skills_and_plan_without_reusing_the_previous_character()
    {
        var arcana = ArcanaAdviceCatalog.Load(Data("arcana"));
        var panel = new ArcanaPanel("Mirror", ArcanaSlot.Mirror, "Magic", "Wisdom [Lumiel]", [new("Impact Hit", 4)], 650);
        var chanter = arcana.For(GameClass.Chanter, panel)!;
        var assassin = arcana.For(GameClass.Assassin, panel)!;
        Assert.True(chanter.MainMatches); Assert.False(assassin.MainMatches); Assert.NotEqual(chanter.Plan, assassin.Plan);
        Assert.Contains(chanter.Skills, s => s.Name == "Wind's Promise"); Assert.DoesNotContain(assassin.Skills, s => s.Name == "Wind's Promise");
        Assert.Equal(chanter, arcana.For(GameClass.Chanter, panel)!, new AdviceIdentityComparer());
        var gear = GearAdviceCatalog.Load(Data("gear")); var item = new ItemTooltip("Bracelet", GearSlot.Bracelet, true, ["Illusion 17"]);
        Assert.Equal("Wisdom", gear.For(GameClass.Chanter, item)!.Targets[0].Name);
        Assert.Equal("Illusion", gear.For(GameClass.Assassin, item)!.Targets[0].Name);
    }

    private sealed class AdviceIdentityComparer : IEqualityComparer<ArcanaAdvice>
    {
        public bool Equals(ArcanaAdvice? a, ArcanaAdvice? b) => a?.ClassLabel == b?.ClassLabel && a?.MainStat == b?.MainStat && a?.Plan == b?.Plan;
        public int GetHashCode(ArcanaAdvice value) => HashCode.Combine(value.ClassLabel, value.MainStat, value.Plan);
    }

    [Fact]
    public void Explicit_but_incomplete_profiles_never_fall_back_to_generic_slots_or_other_classes()
    {
        var gear = new GearAdviceCatalog(new("2026-11-22", "PvE DPS", [], [new("Helmet", "Generic", ["Attack"], [], "None")],
            [new("Chanter", "Chanter", [], [], "", "", Slots: [])]));
        Assert.Null(gear.For(GameClass.Chanter, new("Helm", GearSlot.Helmet, true, [])));
        var arcana = new ArcanaAdviceCatalog(new("2026-11-22", "PvE DPS", [new("Mirror", "Generic", "Vigor", "Illusion [Kaisinel]", "Cooldowns", "")],
            [new("Chanter", "Chanter", "", new() { ["Mirror"] = ["Impact Hit"] }, Cards: [])]));
        Assert.Null(arcana.For(GameClass.Chanter, new("Mirror", ArcanaSlot.Mirror, "Vigor", "Illusion [Kaisinel]", [], 650)));
    }

    [Theory]
    [InlineData(GameClass.Unknown)] [InlineData(GameClass.Brawler)]
    public void Unverified_classes_get_no_invented_build(GameClass gameClass)
    {
        Assert.Null(GearAdviceCatalog.Load(Data("gear")).For(gameClass, new("Helm", GearSlot.Helmet, true, [])));
        Assert.Null(ArcanaAdviceCatalog.Load(Data("arcana")).For(gameClass, new("Mirror", ArcanaSlot.Mirror, "Magic", "Wisdom [Lumiel]", [], 650)));
    }

    [Fact]
    public void Pure_damage_profiles_keep_healing_and_defensive_skills_out_of_primary_goals()
    {
        var cleric = Arcana.Classes.Single(c => c.Class == "Cleric"); var templar = Arcana.Classes.Single(c => c.Class == "Templar");
        Assert.DoesNotContain("Healing Enhancement", cleric.Goals.Values.SelectMany(v => v));
        Assert.DoesNotContain("Radiant Recovery", cleric.Goals.Values.SelectMany(v => v));
        Assert.Equal(["Impact Hit"], templar.Goals["Mirror"]);
        Assert.Contains("Warding Shield", templar.SecondaryGoals!["Mirror"]);
        Assert.Equal("Incandescent Blow", Arcana.Classes.Single(c => c.Class == "Chanter").Goals["Compass"][0]);
        Assert.Contains("Wish of Concentration", Arcana.Classes.Single(c => c.Class == "Sorcerer").Goals["Parchment"]);
    }

    [Fact]
    public void Card_stats_follow_the_global_client_family_mapping_and_not_just_the_profile_label()
    {
        var mapping = new Dictionary<string, string> {
            ["Chalice/Vigor"] = "Time [Siel]", ["Chalice/Magic"] = "Space [Israphel]",
            ["Parchment/Vigor"] = "Life [Yustiel]", ["Parchment/Magic"] = "Destiny [Marchutan]",
            ["Compass/Vigor"] = "Freedom [Vaizel]", ["Compass/Magic"] = "Death [Triniel]",
            ["Bell/Vigor"] = "Justice [Nezekan]", ["Bell/Magic"] = "Destruction [Zikel]",
            ["Mirror/Vigor"] = "Illusion [Kaisinel]", ["Mirror/Magic"] = "Wisdom [Lumiel]"
        };
        Assert.All(Arcana.Classes.SelectMany(c => c.Cards!), card => Assert.Equal(mapping[card.Slot + "/" + card.Family], card.Stat));
        Assert.Equal("Death [Triniel]", Arcana.Classes.Single(c => c.Class == "Ranger").Cards!.Single(c => c.Slot == "Compass").Stat);
        Assert.All(Gear.Classes, c => Assert.Contains("Double Chance", c.Slots!.Single(s => s.Slot == "Shoulders").Targets));
    }
}
