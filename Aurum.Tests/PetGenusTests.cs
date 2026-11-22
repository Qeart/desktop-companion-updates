using AionMeter.Core.Pets;
namespace Aurum.Tests;
public class PetGenusTests
{
    [Theory]
    [InlineData("cogni", true)] [InlineData("fera", true)] [InlineData("natura", false)]
    [InlineData("varian", false)] [InlineData("special", false)]
    public void Multiple_selected_genera_are_combined(string genus, bool expected)
        => Assert.Equal(expected, PetGenusFilter.Includes(new(1, "Pet", [], genus), ["cogni", "fera"]));
    [Fact]
    public void Empty_selection_shows_no_pet_types()
        => Assert.False(PetGenusFilter.Includes(new(1, "Pet", [], "cogni"), []));
    [Fact]
    public void Genus_names_are_case_insensitive()
        => Assert.True(PetGenusFilter.Includes(new(1, "Pet", [], "Cogni"), ["COGNI"]));
    [Fact]
    public void All_known_types_can_be_selected()
    {
        foreach (var genus in PetGenusFilter.Genera)
            Assert.True(PetGenusFilter.Includes(new(1, "Pet", [], genus), PetGenusFilter.Genera));
    }
}
