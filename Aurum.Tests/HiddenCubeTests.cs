using AionMeter.Core.Cubes;
using AionMeter.Core.Events;
using AionMeter.Core.PetMaps;

namespace Aurum.Tests;
public class HiddenCubeTests
{
    private static readonly PlayerMapPosition Player = new(1110, 100, 200, 10, 10000, "OwnEffect");
    private static WorldObjectSeenEvent Cube(uint id, float x, float y, float z = 1000) => new(10000, id, 1301199, x, y, z);
    [Theory]
    [InlineData(10000, 19000, 10, 0)]
    [InlineData(11000, 20000, 10, 90)]
    [InlineData(10000, 21000, 10, 180)]
    [InlineData(9000, 20000, 10, 270)]
    public void Converts_centimetres_to_metres_and_north_up_bearing(float x, float y, double distance, double bearing)
    {
        var tracker = new HiddenCubeTracker(); tracker.Process(Cube(50, x, y, 1600));
        var target = Assert.IsType<HiddenCubeTarget>(tracker.Nearest(Player, 10500));
        Assert.Equal(distance, target.Metres, 6); Assert.Equal(bearing, target.Bearing, 6); Assert.Equal(6, target.HeightMetres);
    }
    [Fact]
    public void Ignores_other_objects_invalid_coordinates_and_distant_cubes()
    {
        var tracker = new HiddenCubeTracker(); tracker.Process(Cube(1, float.NaN, 1));
        tracker.Process(Cube(2, 0, 0, 0)); tracker.Process(Cube(3, 10000, 20000) with { Code = 1301200 });
        Assert.Equal(0, tracker.Count);
        tracker.Process(Cube(4, 25000, 20000)); Assert.Null(tracker.Nearest(Player, 10000));
        Assert.NotNull(tracker.Nearest(Player, 10000, 200));
    }
    [Fact]
    public void Does_not_navigate_without_a_current_local_position_or_across_maps()
    {
        var tracker = new HiddenCubeTracker(); tracker.Process(new ZoneChangedEvent(0, 1110, "", false));
        tracker.Process(Cube(1, 10000, 19000));
        Assert.Null(tracker.Nearest(null, 10000)); Assert.Null(tracker.Nearest(Player, 13001));
        Assert.Null(tracker.Nearest(Player with { MapId = 1010 }, 10500));
        Assert.Null(tracker.Nearest(Player with { At = 20000 }, 10000));
    }
    [Fact]
    public void Despawn_zone_and_teleport_remove_stale_targets()
    {
        var tracker = new HiddenCubeTracker(); tracker.Process(Cube(1, 10000, 19000)); tracker.Process(Cube(2, 10000, 18000));
        Assert.Equal(1u, tracker.Nearest(Player, 10000)!.Id);
        tracker.Process(new WorldObjectGoneEvent(10001, 1)); Assert.Equal(2u, tracker.Nearest(Player, 10001)!.Id);
        tracker.Raw(10002, 0x2336, new byte[18]); Assert.Null(tracker.Nearest(Player, 10002));
        tracker.Process(Cube(3, 10000, 19000)); tracker.Process(new ZoneChangedEvent(10003, 1110, "", false));
        Assert.Null(tracker.Nearest(Player, 10003));
    }
    [Theory]
    [InlineData(0, 350, 10)] [InlineData(350, 0, -10)] [InlineData(90, 180, -90)]
    public void Relative_heading_uses_the_short_turn_across_north(double target, double camera, double expected)
        => Assert.Equal(expected, Navigation.Relative(target, camera));
    [Fact]
    public void Ambiguous_camera_does_not_invent_a_direction()
    {
        Assert.Null(Navigation.DetectCamera(new double[360]));
        Assert.Null(Navigation.DetectCamera(Enumerable.Repeat(20d, 360).ToArray()));
        var scores = new double[360]; scores[310] = 20; scores[50] = 20;
        Assert.Equal(0, Navigation.DetectCamera(scores)!.Value.Heading);
    }
}
