using AionMeter.Core.Events;
using AionMeter.Core.Gear;
using AionMeter.Core.Pets;

namespace Aurum.Tests;

public sealed class OcrSkillNameTests
{
    private static readonly string[] Skills = ["Incandescent Blow", "Impactful Crush", "Impact Hit", "Earth's Promise"];
    [Theory]
    [InlineData("Incandescent Blow")]
    [InlineData("lncandescent Blow")]
    [InlineData("1ncandescent B1ow")]
    [InlineData("Incandescenł Blow")]
    [InlineData("lncandescent Błow")]
    [InlineData("gł lncandescenł Błow")]
    [InlineData("[+] Incandescent Blow")]
    [InlineData("INCANDESCENT BLOW")]
    public void Recognises_complete_names_with_specific_ocr_glyph_errors(string observed)
        => Assert.Equal("Incandescent Blow", OcrSkillNames.Find(observed, Skills));
    [Theory]
    [InlineData("Incandescent")]
    [InlineData("Incandescent…")]
    [InlineData("Incandescent Blast")]
    [InlineData("Incandescent Glow")]
    [InlineData("Not Incandescent Blow")]
    [InlineData("")]
    public void Partial_or_different_names_are_not_reported_as_present(string observed)
        => Assert.Null(OcrSkillNames.Find(observed, Skills));
    [Fact]
    public void Ambiguous_glyph_match_is_rejected()
        => Assert.Null(OcrSkillNames.Find("1mpact Hit", ["Impact Hit", "lmpact Hit"]));
    [Theory]
    [InlineData("lncandescent Blow")]
    [InlineData("gł Incandescenł Blow")]
    public void Arcana_advice_marks_the_skill_present_and_preserves_its_read_bonus(string observed)
    {
        ShopText[] rows = [new("Compass Item Level 80", 20, 20, 200, 14), new("+5 Compass of Vigor", 20, 60, 220, 20),
            new("Equipped Effect", 20, 170, 150, 14), new("Freedom [Vaizel]", 20, 220, 150, 14),
            new(observed, 60, 260, 180, 14), new("Lv. +2", 300, 260, 60, 14),
            new("Set Effects", 20, 400, 150, 14), new("Primal Vigor", 20, 440, 150, 14)];
        var panel = Assert.IsType<ArcanaPanel>(ArcanaPanelParser.Parse(rows));
        var catalog = new ArcanaAdviceCatalog(new("2026-11-22", "PvE DPS",
            [new("Compass", "Kompas", "Vigor", "Freedom [Vaizel]", "Accuracy", "Death")],
            [new("Chanter", "Chanter", "", new() { ["Compass"] = ["Incandescent Blow", "Impactful Crush"] })]));
        var advice = catalog.For(GameClass.Chanter, panel)!;
        var skill = advice.Skills.Single(s => s.Name == "Incandescent Blow");
        Assert.True(skill.Present); Assert.Equal(2, skill.Bonus);
        Assert.False(advice.Skills.Single(s => s.Name == "Impactful Crush").Present);
    }
}
