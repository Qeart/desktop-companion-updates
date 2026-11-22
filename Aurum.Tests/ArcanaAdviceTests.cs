using AionMeter.Core.Events;
using AionMeter.Core.Gear;
using AionMeter.Core.Pets;

namespace Aurum.Tests;
public class ArcanaAdviceTests
{
    private static ShopText Text(string value, double y, double x = 20) => new(value, x, y, 150, 14);
    private static ShopText[] ScreenshotRows => [Text("Chalice Item Level 80(+5)", 20), Text("+5 Chalice of Vigor", 60),
        Text("Equipped Effect", 175), Text("Time [Siel]", 225), Text("25", 225, 330), Text("g Onslaught", 260), Text("Lv. +4", 260, 310),
        Text("Earth's Promise", 292), Text("Lv. +2", 292, 310), Text("Dark Crush", 324), Text("Lv. +1", 324, 310),
        Text("Impactful Crush", 356), Text("Lv. +2", 356, 310), Text("Set Effects", 400), Text("Primal Vigor", 445),
        Text("+60 PvE Attack when HP is 70% or more", 490), Text("+150 PvE Attack when HP is 70% or more", 525)];
    [Fact]
    public void Reads_selected_card_stat_skills_levels_and_anchor_below_set_effects()
    {
        var panel = Assert.IsType<ArcanaPanel>(ArcanaPanelParser.Parse(ScreenshotRows));
        Assert.Equal(ArcanaSlot.Chalice, panel.Slot); Assert.Equal("Vigor", panel.Family); Assert.Equal("Time [Siel]", panel.MainStat);
        Assert.Equal(4, panel.Skills.Single(s => s.Name == "g Onslaught").Bonus);
        Assert.Equal(1, panel.Skills.Single(s => s.Name == "Dark Crush").Bonus); Assert.Equal(539, panel.SetBottom);
        Assert.DoesNotContain(panel.Skills, s => s.Name.Contains("PvE") || s.Name.Contains("Primal"));
    }
    [Theory]
    [InlineData("Arcana Equip Extract Transmute", true)] [InlineData("Arcana Eguip Extract Transrnute", true)]
    [InlineData("Special Pets Skins", false)] [InlineData("Arcana", false)] [InlineData("Equip Transmute", false)]
    public void Requires_the_complete_arcana_screen_context(string text, bool expected) => Assert.Equal(expected, ArcanaPanelParser.IsHeader(text));
    [Fact]
    public void Missing_selection_or_set_effects_is_not_a_selected_arcana()
    {
        Assert.Null(ArcanaPanelParser.Parse([]));
        Assert.Null(ArcanaPanelParser.Parse(ScreenshotRows.Where(r => r.Text != "Set Effects").ToArray()));
        Assert.Null(ArcanaPanelParser.Parse(ScreenshotRows.Where(r => r.Text != "Equipped Effect").ToArray()));
    }
    [Fact]
    public void Item_category_line_recovers_the_card_type_when_the_decorative_title_is_misread()
    {
        var rows = ScreenshotRows.Select(r => r with { Text = r.Text.Replace("+5 Chalice", "+5 Chaljce") }).ToArray();
        Assert.Equal(ArcanaSlot.Chalice, ArcanaPanelParser.Parse(rows)!.Slot);
    }
    [Theory]
    [InlineData("+5 Parchment of Vig...")]
    [InlineData("+5 Parchment of…")]
    [InlineData("+5 Parchment of Vig")]
    [InlineData("+5 Parch...")]
    public void Clipped_name_does_not_block_card_advice_or_invent_its_family(string title)
    {
        var rows = ScreenshotRows.Select(r => r with { Text = r.Text.Replace("Chalice", "Parchment") }).ToArray();
        rows[1] = Text(title, 60);
        var panel = Assert.IsType<ArcanaPanel>(ArcanaPanelParser.Parse(rows));
        Assert.Equal(ArcanaSlot.Parchment, panel.Slot); Assert.Equal(title, panel.Title); Assert.Equal("", panel.Family);
        Assert.Contains(panel.Skills, s => s.Name == "Dark Crush"); Assert.Equal(539, panel.SetBottom);
        var catalog = new ArcanaAdviceCatalog(new("2026-11-22", "PvE DPS",
            [new("Parchment", "Pergamin", "Vigor", "Life [Yustiel]", "HP", "Destiny")],
            [new("Chanter", "Chanter", "", new() { ["Parchment"] = ["Dark Crush"] })]));
        Assert.Equal("Life [Yustiel]", catalog.For(GameClass.Chanter, panel)!.MainStat);
    }
    [Fact]
    public void Category_alone_recovers_a_fully_unreadable_decorative_name()
    {
        var panel = Assert.IsType<ArcanaPanel>(ArcanaPanelParser.Parse(ScreenshotRows.Where((_, i) => i != 1).ToArray()));
        Assert.Equal(ArcanaSlot.Chalice, panel.Slot); Assert.Equal("Chalice", panel.Title); Assert.Equal("", panel.Family);
    }
    [Fact]
    public void Unique_clipped_type_works_when_the_category_line_is_unreadable()
    {
        var rows = ScreenshotRows.Where((_, i) => i != 0).Select(r => r.Text == "+5 Chalice of Vigor" ? Text("+5 Parch…", 60) : r).ToArray();
        Assert.Equal(ArcanaSlot.Parchment, ArcanaPanelParser.Parse(rows)!.Slot);
    }
    [Fact]
    public void Neither_unsupported_card_types_nor_conflicting_categories_are_guessed()
    {
        Assert.Null(ArcanaPanelParser.Parse(ScreenshotRows.Select(r => r with { Text = r.Text.Replace("Chalice", "Unknown") }).ToArray()));
        Assert.Null(ArcanaPanelParser.Parse([..ScreenshotRows, Text("Parchment Item Level 80", 35)]));
    }
    [Fact]
    public void Unrelated_text_far_below_set_effects_does_not_move_the_advice_anchor()
    {
        var panel = ArcanaPanelParser.Parse([..ScreenshotRows, Text("Other overlay", 800)])!;
        Assert.Equal(539, panel.SetBottom);
    }
    [Theory]
    [InlineData("Parchment", ArcanaSlot.Parchment)] [InlineData("Compass", ArcanaSlot.Compass)]
    [InlineData("Bell", ArcanaSlot.Bell)] [InlineData("Mirror", ArcanaSlot.Mirror)]
    public void Recognises_all_five_card_types(string label, ArcanaSlot expected)
    {
        var rows = ScreenshotRows.Select(r => r with { Text = r.Text.Replace("Chalice", label) }).ToArray();
        Assert.Equal(expected, ArcanaPanelParser.Parse(rows)!.Slot);
    }
    [Fact]
    public void Class_and_card_choose_only_that_cards_goals_and_existing_bonuses()
    {
        var catalog = new ArcanaAdviceCatalog(new("2026-11-22", "PvE DPS", [new("Chalice", "Kielich", "Vigor", "Time [Siel]", "Combat Speed", "Space")],
            [new("Chanter", "Chanter", "", new() { ["Chalice"] = ["Dark Crush", "Spinning Strike", "Onslaught"] })]));
        var panel = ArcanaPanelParser.Parse(ScreenshotRows)!; var advice = catalog.For(GameClass.Chanter, panel)!;
        Assert.True(advice.MainMatches); Assert.True(advice.Skills.Single(s => s.Name == "Dark Crush").Present);
        Assert.Equal(4, advice.Skills.Single(s => s.Name == "Onslaught").Bonus); Assert.False(advice.Skills.Single(s => s.Name == "Spinning Strike").Present);
        Assert.Null(catalog.For(GameClass.Unknown, panel)); Assert.Null(catalog.For(GameClass.Assassin, panel));
        Assert.False(catalog.For(GameClass.Chanter, panel with { MainStat = "Space [Israphel]", Family = "Magic" })!.MainMatches);
    }
}
