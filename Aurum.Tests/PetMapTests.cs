using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;
namespace Aurum.Tests;
public class PetMapTests
{
    private static PetWorldMap Map(Dictionary<int, double[]>? spawns = null) => new(1110, "Altgard", 1000, 1000, [-50000, -50000, 50000, 50000], spawns ?? []);
    [Fact]
    public void World_metres_use_centimetre_bounds_and_keep_both_axis_directions()
    {
        var map = Map();
        Assert.Equal((500d, 500d), map.WorldToImage(0, 0));
        Assert.Equal((0d, 0d), map.WorldToImage(-500, -500));
        Assert.Equal((1000d, 1000d), map.WorldToImage(500, 500));
        Assert.Equal((600d, 300d), map.WorldToImage(100, -200));
    }
    [Fact]
    public void Projected_positions_follow_map_zoom_and_pan()
    {
        var fit = new PetMapAlignment(1110, 2, 0, 100, -200, 30, 50);
        Assert.True(fit.IsPlausible); Assert.Equal((1100d, 800d), fit.Project(500, 500)); Assert.Equal(2, fit.Scale);
    }
    [Theory]
    [InlineData(1, 0, 12, -8)]
    [InlineData(1.1, 0, -50, 20)]
    [InlineData(.999, .001, 3, 4)]
    public void Incremental_tracking_composes_zoom_and_pan_in_the_correct_order(double a, double b, double tx, double ty)
    {
        var first = new PetMapAlignment(1110, 2, .003, 100, -200, 30, 50);
        var before = first.Project(270, 380);
        var next = first.Follow(a, b, tx, ty, 30, 40);
        var after = next.Project(270, 380);
        Assert.Equal(a * before.X - b * before.Y + tx, after.X, 8);
        Assert.Equal(b * before.X + a * before.Y + ty, after.Y, 8);
    }
    [Theory]
    [InlineData(1, 0, 17, 50)]
    [InlineData(1, 0, 18, 100)]
    [InlineData(-1, 0, 30, 50)]
    [InlineData(.01, 0, 30, 50)]
    [InlineData(1, .2, 30, 50)]
    public void Rejects_weak_rotated_or_impossible_map_fits(double a, double b, int inliers, int matches)
        => Assert.False(new PetMapAlignment(1110, a, b, 0, 0, inliers, matches).IsPlausible);
    [Fact]
    public void Areas_include_only_selected_pets_sources_and_separate_far_groups()
    {
        var catalog = new PetMapCatalog([Map(new() { [7] = [0, 0, 10, 10, 1000, 1000], [8] = [500, 500] })]);
        var pet = new PetDefinition(1, "Pet", [new(7, "Mob", 1)]);
        var areas = catalog.Areas(1110, pet);
        Assert.Equal(2, areas.Count); Assert.Equal(3, areas.Sum(a => a.Points)); Assert.Equal(5, areas[0].XMetres); Assert.Equal(5, areas[0].YMetres);
        Assert.True(areas.All(a => a.RadiusMetres >= 30)); Assert.True(catalog.HasSpawns(1110, pet));
        Assert.Empty(catalog.Areas(9999, pet));
        Assert.Empty(catalog.Areas(1110, new(2, "Unknown", [new(9, "Other", 1)])));
    }
    [Fact]
    public void Repeated_source_records_do_not_duplicate_spawn_points()
    {
        var catalog = new PetMapCatalog([Map(new() { [7] = [0, 0, 0, 0, 5, 5] })]);
        var pet = new PetDefinition(1, "Pet", [new(7, "Mob", 1), new(7, "Mob", .5)]);
        Assert.Equal(2, Assert.Single(catalog.Areas(1110, pet)).Points);
    }
    [Fact]
    public void Maps_without_valid_coordinate_bounds_are_not_loaded()
        => Assert.Empty(new PetMapCatalog([Map() with { Bounds = [0, 0, 0, 0] }]).Maps);
}
