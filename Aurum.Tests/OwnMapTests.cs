using System.Buffers.Binary;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Events;

namespace Aurum.Tests;

public class OwnMapTests
{
    [Fact]
    public void Reversing_a_drag_returns_every_world_point_to_its_original_pixel()
    {
        var camera = new MapViewport(); camera.Fit(2048, 2048, 1100, 750);
        var before = camera.Project(723.4, 987.6);
        for (var i = 0; i < 100; i++) { camera.Pan(160, -90); camera.Pan(-160, 90); }
        var after = camera.Project(723.4, 987.6);
        Assert.Equal(before.X, after.X, 8); Assert.Equal(before.Y, after.Y, 8);
    }
    [Fact]
    public void Drag_changes_screen_anchor_without_changing_scale_or_world_coordinates()
    {
        var camera = new MapViewport(); camera.Fit(2048, 2048, 1100, 750);
        var scale = camera.Scale; var before = camera.Project(1000, 800);
        camera.Pan(-120, 35); var after = camera.Project(1000, 800);
        Assert.Equal(before.X - 120, after.X, 8); Assert.Equal(before.Y + 35, after.Y, 8); Assert.Equal(scale, camera.Scale);
        var world = camera.Unproject(after.X, after.Y); Assert.Equal(1000, world.X, 8); Assert.Equal(800, world.Y, 8);
    }
    [Fact]
    public void Zoom_keeps_the_point_under_the_cursor_at_the_same_pixel_after_a_drag()
    {
        var camera = new MapViewport(); camera.Fit(2048, 2048, 1100, 750); camera.Pan(160, -90);
        var world = camera.Unproject(477, 333); camera.Zoom(1.3, 477, 333);
        var pixel = camera.Project(world.X, world.Y); Assert.Equal(477, pixel.X, 8); Assert.Equal(333, pixel.Y, 8);
    }
    [Fact]
    public void Player_and_pet_at_the_same_world_point_share_one_projection_at_any_zoom()
    {
        var map = new PetWorldMap(1110, "Map", 2048, 2048, [-100000, -100000, 100000, 100000], []);
        var point = map.WorldToImage(123, -456); var camera = new MapViewport();
        camera.Fit(map.Width, map.Height, 1000, 700); camera.Pan(120, 35); camera.Zoom(1.8, 430, 320);
        var screen = camera.Project(point.X, point.Y); var recovered = camera.Unproject(screen.X, screen.Y);
        Assert.Equal(point.X, recovered.X, 8); Assert.Equal(point.Y, recovered.Y, 8);
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)] [InlineData(0)]
    public void Invalid_zoom_cannot_corrupt_the_camera(double factor)
    {
        var camera = new MapViewport(); var before = camera.Project(100, 200); camera.Zoom(factor, 50, 60);
        Assert.Equal(before, camera.Project(100, 200));
    }
    private static readonly byte[] Effect = Convert.FromHexString("C9441112844E000023FBBE8700000000B199A67DA2010000C9440A02D067F5478E34804600000343");
    [Fact]
    public void Own_effect_produces_metres_and_becomes_stale_without_new_packets()
    {
        var tracker = new PlayerPositionTracker(); tracker.SeedIdentity(8777, 1110); tracker.Raw(10000, 0x2B38, Effect);
        var point = Assert.IsType<PlayerMapPosition>(tracker.Snapshot());
        Assert.Equal(1110, point.MapId); Assert.Equal(1256.47625, point.XMetres, 6);
        Assert.True(point.Fresh(12000)); Assert.False(point.Fresh(14000)); Assert.Equal(4000, point.Age(14000));
        Assert.Equal(point, tracker.Snapshot());
    }
    [Fact]
    public void Another_players_effect_and_truncated_packets_do_not_place_the_player()
    {
        var tracker = new PlayerPositionTracker(); tracker.SeedIdentity(1234, 1110); tracker.Raw(10000, 0x2B38, Effect); Assert.Null(tracker.Snapshot());
        tracker.SeedIdentity(8777, 1110);
        for (var size = 0; size < Effect.Length; size++) { tracker.Raw(10000, 0x2B38, Effect.AsSpan(0, size)); Assert.Null(tracker.Snapshot()); }
    }
    [Fact]
    public void Map_load_position_survives_the_zone_event_from_that_same_packet()
    {
        var tracker = new PlayerPositionTracker(); var body = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 1010);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(16), 12345); BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(20), -9876); BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(24), 100);
        tracker.Raw(10000, 0x2136, body); tracker.Process(new ZoneChangedEvent(10000, 1010, "Verteron", false, false));
        var point = Assert.IsType<PlayerMapPosition>(tracker.Snapshot()); Assert.Equal(1010, point.MapId); Assert.Equal(123.45, point.XMetres, 6); Assert.Equal(-98.76, point.YMetres, 6);
    }
    [Fact]
    public void A_different_region_or_character_discards_the_old_marker()
    {
        var tracker = new PlayerPositionTracker(); tracker.SeedIdentity(8777, 1110); tracker.Raw(10000, 0x2B38, Effect);
        tracker.Process(new ZoneChangedEvent(12000, 1010, "Verteron", false, false)); Assert.Null(tracker.Snapshot());
        tracker.Raw(13000, 0x2B38, Effect); Assert.NotNull(tracker.Snapshot());
        tracker.Process(new SelfIdentifiedEvent(14000, 1234, "Other", 2314, GameClass.Chanter)); Assert.Null(tracker.Snapshot());
    }
    [Fact]
    public void Invalid_coordinates_and_older_updates_do_not_replace_a_known_position()
    {
        var tracker = new PlayerPositionTracker(); tracker.SeedIdentity(8777, 1110); tracker.Raw(10000, 0x2B38, Effect); var first = tracker.Snapshot();
        var invalid = Effect.ToArray(); BinaryPrimitives.WriteSingleLittleEndian(invalid.AsSpan(invalid.Length - 12), float.NaN);
        tracker.Raw(11000, 0x2B38, invalid); Assert.Equal(first, tracker.Snapshot());
        tracker.Raw(9000, 0x2B38, Effect); Assert.Equal(first, tracker.Snapshot());
    }
}
