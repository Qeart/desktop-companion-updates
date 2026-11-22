using AionMeter.Core.Gear;

namespace Aurum.Tests;
public class TooltipSelectionTests
{
    [Theory]
    [InlineData(1048, 728)] [InlineData(1110, 90)] [InlineData(50, 50)]
    public void Inventory_comparison_selects_only_the_right_tooltip_even_with_a_distant_cursor(double x, double y)
    {
        TooltipBox[] boxes = [new(0, 0, 357, 688), new(366, 0, 354, 555)];
        var selected = Assert.Single(TooltipSelection.Order(boxes, x, y));
        Assert.Equal(1, selected.Index); Assert.True(selected.ComparisonRight);
    }
    [Fact]
    public void Comparison_selection_does_not_depend_on_contour_enumeration_order()
    {
        TooltipBox[] boxes = [new(366, 0, 354, 555), new(0, 0, 357, 688)];
        Assert.Equal(0, Assert.Single(TooltipSelection.Order(boxes, 1048, 728)).Index);
    }
    [Fact]
    public void Single_equipped_or_inventory_tooltip_still_works()
    {
        var selected = Assert.Single(TooltipSelection.Order([new(8, 0, 355, 590)], 410, 90));
        Assert.False(selected.ComparisonRight); Assert.Equal(0, selected.Index);
    }
    [Fact]
    public void Unrelated_distant_windows_are_not_a_comparison_pair()
    {
        var selected = TooltipSelection.Order([new(0, 0, 355, 600), new(700, 300, 355, 600)], 1100, 500);
        Assert.All(selected, choice => Assert.False(choice.ComparisonRight)); Assert.Equal(1, selected[0].Index);
    }
    [Fact]
    public void Scaled_comparison_keeps_the_same_target()
    {
        var selected = Assert.Single(TooltipSelection.Order([new(0, 0, 535.5, 1032), new(549, 0, 531, 832.5)], 1572, 1092, 1.5));
        Assert.True(selected.ComparisonRight); Assert.Equal(1, selected.Index);
    }
}
