using AionMeter.Core.Pets;
namespace Aurum.Tests;
public class ShopGroupingTests
{
    [Fact]
    public void Combines_fragmented_title_but_keeps_neighbouring_cards_separate()
    {
        ShopText[] words = [new("Pet:", 10, 100, 25, 12), new("Cave", 40, 101, 30, 11), new("Bat", 75, 100, 20, 12),
            new("Pet:", 250, 100, 25, 12), new("Armadon", 280, 100, 55, 12), new("Owned", 40, 120, 40, 12)];
        var grouped = ShopTextGrouping.Group(words);
        Assert.Contains(grouped, r => r.Text == "Pet: Cave Bat" && r.X == 10);
        Assert.Contains(grouped, r => r.Text == "Pet: Armadon" && r.X == 250);
        Assert.Contains(grouped, r => r.Text == "Owned"); Assert.Equal(3, grouped.Count);
    }
    [Theory]
    [InlineData("Special Pets Skins", true)]
    [InlineData("Special Pełs Skins", true)]
    [InlineData("Shop Pets", false)]
    [InlineData("Pet: Cave Bat", false)]
    public void Recognizes_central_shop_tabs_without_needing_obscured_shop_title(string text, bool expected)
        => Assert.Equal(expected, ShopTextGrouping.IsShopHeader(text));
}
