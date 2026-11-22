using AionMeter.Core.Events;
using AionMeter.Core.Gear;
using AionMeter.Core.Pets;
using AionMeter.Core.Combat;
using AionMeter.Core.Game;

namespace Aurum.Tests;
public class ItemAdviceTests
{
    [Fact]
    public void Advice_identity_uses_own_class_and_survives_a_meter_restart()
    {
        var tracker = new CombatTracker(GameData.Empty, new());
        tracker.Process(new PlayerSeenEvent(0, 2, "Other", 2314, GameClass.Sorcerer));
        Assert.Equal(GameClass.Unknown, tracker.SelfClass);
        tracker.Process(new SelfIdentifiedEvent(1, 1, "Self", 2314, GameClass.Chanter));
        Assert.Equal(GameClass.Chanter, tracker.SelfClass);
        var restored = new CombatTracker(GameData.Empty, new()); restored.ImportCache(tracker.ExportCache());
        Assert.Equal(GameClass.Chanter, restored.SelfClass);
        restored.Process(new SelfIdentifiedEvent(2, 3, "Alt", 2314, GameClass.Assassin));
        Assert.Equal(GameClass.Assassin, restored.SelfClass);
    }
    private static ShopText Text(string value, double y) => new(value, 15, y, 240, 14);
    [Theory]
    [InlineData("Item Level")] [InlineData("Iłem Level")] [InlineData("ltem Level")] [InlineData("Itern Level")]
    public void Recognises_observed_windows_ocr_variants_of_item_level(string text) => Assert.True(ItemTooltipParser.IsItemLevel(text));
    [Fact]
    public void Skill_icon_ocr_prefix_does_not_hide_a_valid_class_bonus()
        => Assert.True(ItemTooltipParser.HasSkill(Helm(Text("g Inspiring Spell Lv. +1", 244)), "Inspiring Spell"));
    private static ItemTooltip Helm(params ShopText[] body) => ItemTooltipParser.Parse([
        Text("+10 Nuakum Helm", 10), Text("Unique Helm", 34), Text("Item Level", 58), Text("Attack 200", 150), Text("Soul Binding Rate 60.7%", 210), ..body,
        Text("Soul Fuse Philosopher's Stone", 353), Text("Attack+10", 440), Text("Required Level 45", 550)])!;
    [Fact]
    public void Recognises_screenshot_helmet_without_counting_base_stats_or_sockets()
    {
        var helm = Helm(Text("Natural HP Regen 45", 244), Text("Dexterity 12", 268), Text("Weapon Damage Tolerance 6.9%", 292), Text("Inspiring Spell Lv. +1", 316));
        Assert.Equal(GearSlot.Helmet, helm.Slot); Assert.True(helm.BindingRead); Assert.Equal("+10 Nuakum Helm", helm.Title);
        Assert.False(ItemTooltipParser.HasStat(helm, "Attack")); Assert.False(ItemTooltipParser.HasStat(helm, "Weapon Damage Boost"));
        Assert.True(ItemTooltipParser.HasSkill(helm, "Inspiring Spell"));
    }
    [Fact]
    public void Attack_increase_and_a_passive_do_not_count_as_flat_attack()
    {
        var helm = Helm(Text("Attack increase 2.0%", 244), Text("Attack Preparation Lv. +1", 268));
        Assert.True(ItemTooltipParser.HasStat(helm, "Attack increase")); Assert.False(ItemTooltipParser.HasStat(helm, "Attack"));
        Assert.True(ItemTooltipParser.HasSkill(helm, "Attack Preparation"));
    }
    [Fact]
    public void Missing_fuse_heading_still_stops_at_the_socket_section_gap()
    {
        var helm = ItemTooltipParser.Parse([Text("Unique Helm", 20), Text("Item Level", 50), Text("Soul Binding Rate", 210),
            Text("Dexterity 12", 244), Text("Weapon Damage Tolerance 6.9%", 268), Text("Attack+10", 400), Text("Skin Info", 480)])!;
        Assert.False(ItemTooltipParser.HasStat(helm, "Attack"));
    }
    [Fact]
    public void Missing_binding_does_not_report_base_attack_as_a_roll()
    {
        var item = ItemTooltipParser.Parse([Text("Unique Staff", 10), Text("Item Level", 35), Text("Attack 500", 150)])!;
        Assert.False(item.BindingRead); Assert.False(ItemTooltipParser.HasStat(item, "Attack"));
    }
    [Fact]
    public void Type_line_wins_over_item_names_containing_another_slot()
    {
        var item = ItemTooltipParser.Parse([Text("Swordmaster's Guardian Helm Ring", 10), Text("Unique Ring", 32), Text("Item Level", 58)])!;
        Assert.Equal(GearSlot.Ring, item.Slot);
    }
    [Theory]
    [InlineData("Unique Staff", GearSlot.Weapon)] [InlineData("Epic Spellbook", GearSlot.Weapon)]
    [InlineData("Unique Shield", GearSlot.OffHand)] [InlineData("Unique Pauldrons", GearSlot.Shoulders)]
    [InlineData("Unique Breastplate", GearSlot.Chest)] [InlineData("Unique Greaves", GearSlot.Legs)]
    [InlineData("Unique Gloves", GearSlot.Gloves)] [InlineData("Unique Boots", GearSlot.Boots)]
    [InlineData("Unique Cloak", GearSlot.Cloak)] [InlineData("Unique Necklace", GearSlot.Necklace)]
    [InlineData("Unique Earrings", GearSlot.Earrings)] [InlineData("Unique Bracelet", GearSlot.Bracelet)]
    public void Detects_slot_from_equipment_type(string name, GearSlot expected) => Assert.Equal(expected, ItemTooltipParser.Classify(name));
    [Fact]
    public void Consumable_skill_and_empty_tooltips_are_rejected()
    {
        Assert.Null(ItemTooltipParser.Parse([Text("Healing Potion", 10), Text("Required Level 45", 50)]));
        Assert.Null(ItemTooltipParser.Parse([Text("Dark Crush", 10), Text("Attack 500", 30)]));
        Assert.Null(ItemTooltipParser.Parse([]));
    }
    [Fact]
    public void Class_profile_selects_passives_for_helmet_and_actives_for_ring()
    {
        var catalog = new GearAdviceCatalog(new("2026-11-22", "PvE DPS", [], [
            new("Helmet", "Hełm", ["Double Chance", "Attack"], [], "Passive"), new("Ring", "Pierścień", ["Attack"], [], "Active")],
            [new("Chanter", "Chanter", ["Dark Crush"], ["Wind's Promise"], "", "") ]));
        var helm = Helm(Text("Double Chance 2.0%", 244), Text("Attack 28", 268), Text("Wind's Promise Lv. +1", 292));
        var advice = catalog.For(GameClass.Chanter, helm)!;
        Assert.All(advice.Targets, t => Assert.True(t.Present)); Assert.Equal("Wind's Promise", Assert.Single(advice.Skills).Name);
        Assert.Equal("Dark Crush", Assert.Single(catalog.For(GameClass.Chanter, helm with { Slot = GearSlot.Ring })!.Skills).Name);
        Assert.Null(catalog.For(GameClass.Unknown, helm)); Assert.Null(catalog.For(GameClass.Cleric, helm));
    }
}
